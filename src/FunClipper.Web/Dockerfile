FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY FunClipper.Web.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish --no-restore -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV FUNCLIPPER_SHARE_DIR=/data
EXPOSE 8080
VOLUME /data
ENTRYPOINT ["dotnet", "FunClipper.Web.dll"]
