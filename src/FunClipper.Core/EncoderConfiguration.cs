namespace FunClipper.Core;

internal record EncoderConfiguration(string Name, bool GpuScaling, bool FrameCopy = false)
{
    internal string Description => Name switch
    {
        "h264_amf" => "AMD AMF",
        "h264_nvenc" => "NVIDIA NVENC",
        "h264_qsv" => "Intel Quick Sync",
        _ => "CPU · 2 threads"
    } + (Name == "libx264" ? "" : GpuScaling ? " · GPU resize" : " · CPU resize") +
        (FrameCopy ? " · frame copy" : "");

    internal string CaptureFilter(ProfileSettings profile)
    {
        // Preserve the full display and its aspect ratio; pad unusual aspect ratios rather than crop.
        if (FrameCopy)
            // Explicit hardware output avoids AMF's unreliable software-output negotiation.
            // Padding uses reduced NV12 frames; this route copies frames between GPU and RAM.
            return $"hwdownload,format=bgra,vpp_amf=w={profile.Width}:h={profile.Height}:format=nv12:" +
                "force_original_aspect_ratio=decrease:force_divisible_by=2:reset_sar=1," +
                $"format=amf,hwdownload,format=nv12,pad={profile.Width}:{profile.Height}:(ow-iw)/2:(oh-ih)/2,setsar=1";
        if (!GpuScaling)
            return $"hwdownload,format=bgra,scale={profile.Width}:{profile.Height}:force_original_aspect_ratio=decrease:force_divisible_by=2," +
                $"pad={profile.Width}:{profile.Height}:(ow-iw)/2:(oh-ih)/2,format=nv12";
        return $"scale_d3d11=width={profile.Width}:height={profile.Height}:format=nv12" +
            (Name == "h264_qsv" ? ",hwmap=derive_device=qsv,format=qsv" : "");
    }

    internal static IReadOnlyList<string> ExportArguments(string encoder) => encoder switch
    {
        "h264_amf" => ["-usage", "ultralowlatency", "-quality", "speed", "-rc", "cbr", "-b:v", "8000k", "-bf", "0"],
        "h264_nvenc" => ["-preset", "p1", "-tune", "ull", "-rc", "vbr", "-cq", "23", "-bf", "0"],
        "h264_qsv" => ["-preset", "veryfast", "-global_quality", "23", "-bf", "0"],
        _ => ["-threads", "2", "-preset", "ultrafast", "-crf", "22"]
    };

    internal IReadOnlyList<string> CaptureArguments(ProfileSettings profile)
    {
        // Desktop/GPU filter timestamps can repeat; normalize before encoding and segment muxing.
        List<string> args = ["-c:v", Name, "-b:v", profile.Bitrate, "-maxrate", profile.Bitrate,
            "-bufsize", profile.Bitrate, "-g", profile.FrameRate.ToString(), "-bf", "0",
            "-r", profile.FrameRate.ToString(), "-fps_mode", "cfr"];
        args.AddRange(Name switch
        {
            // Segment muxers need independently decodable IDRs, not ordinary I-frames.
            // AMF defaults forced_idr off; without this a live replay can remain one unclosed file forever.
            "h264_amf" => ["-usage", "ultralowlatency", "-quality", "speed", "-rc", "cbr", "-async_depth", "2", "-forced_idr", "1"],
            "h264_nvenc" => ["-preset", "p1", "-tune", "ull", "-rc", "cbr", "-rc-lookahead", "0", "-surfaces", "4", "-forced-idr", "1"],
            "h264_qsv" => ["-preset", "veryfast", "-async_depth", "2", "-look_ahead", "0", "-forced_idr", "1"],
            _ => ["-threads", "2", "-preset", "ultrafast", "-tune", "zerolatency", "-sc_threshold", "0"]
        });
        args.AddRange(["-force_key_frames", "expr:gte(t,n_forced*1)"]);
        return args;
    }

    internal static async Task<EncoderConfiguration> SelectAsync(CaptureProfile profile,
        Action<string>? report, CancellationToken ct)
    {
        var settings = ProfileSettings.For(profile);
        foreach (var name in new[] { "h264_amf", "h264_nvenc", "h264_qsv" })
        {
            report?.Invoke($"Checking {name} with synthetic frames…");
            // Test the practical encoder before trying its direct GPU scaling route.
            var softwareInput = new EncoderConfiguration(name, false);
            if (!await ProbeAsync(softwareInput, settings, ct, report)) continue;
            if (name == "h264_amf")
            {
                // AMF VPP can work even when the Direct3D11 scaling filter does not.
                var copiedGpuInput = new EncoderConfiguration(name, true, FrameCopy: true);
                return await ProbeAsync(copiedGpuInput, settings, ct) ? copiedGpuInput : softwareInput;
            }
            var gpuInput = new EncoderConfiguration(name, true);
            return await ProbeAsync(gpuInput, settings, ct) ? gpuInput : softwareInput;
        }
        if (profile != CaptureProfile.Minimum)
            throw new InvalidOperationException("No working hardware H.264 encoder was found. Select Minimum for an explicit 480p CPU recording option, or update your GPU driver.");
        var cpu = new EncoderConfiguration("libx264", false);
        if (!await ProbeAsync(cpu, settings, ct))
            throw new InvalidOperationException("FFmpeg cannot encode H.264 on this PC. Check the FFmpeg installation.");
        return cpu;
    }

    internal static async Task<bool> ProbeAsync(EncoderConfiguration encoder, ProfileSettings settings, CancellationToken ct,
        Action<string>? report = null)
    {
        List<string> args = ["-hide_banner", "-loglevel", "error", "-threads", "2", "-filter_threads", "2"];
        if (encoder.FrameCopy)
            args.AddRange(["-init_hw_device", "d3d11va=fc", "-init_hw_device", "amf=af@fc", "-filter_hw_device", "af"]);
        else if (encoder.GpuScaling) args.AddRange(["-init_hw_device", "d3d11va=fc", "-filter_hw_device", "fc"]);
        // The copied AMF path must perform a real resize with a different display aspect ratio.
        var sourceSize = encoder.FrameCopy ? "1600x1200" : $"{settings.Width}x{settings.Height}";
        args.AddRange(["-f", "lavfi", "-i", $"color=c=black:size={sourceSize}:rate={settings.FrameRate}"]);
        if (encoder.GpuScaling)
            args.AddRange(["-vf", "format=bgra,hwupload" + (encoder.FrameCopy ? "=derive_device=d3d11va" : "") + "," + encoder.CaptureFilter(settings)]);
        else args.AddRange(["-vf", "format=nv12"]);
        args.AddRange(["-frames:v", "8"]);
        args.AddRange(encoder.CaptureArguments(settings));
        args.AddRange(["-an", "-f", "null", "-"]);
        try
        {
            var result = await ProcessRunner.RunAsync(MediaTools.FindFfmpeg(), args, ct, TimeSpan.FromSeconds(12));
            if (result.ExitCode != 0)
                report?.Invoke($"{encoder.Name} unavailable: {result.StandardError.Trim().Split('\n').FirstOrDefault()}");
            return result.ExitCode == 0;
        }
        catch (TimeoutException) { report?.Invoke($"{encoder.Name} availability check timed out."); return false; }
    }
}
