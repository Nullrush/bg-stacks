FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY src/BgStacks.Web/BgStacks.Web.csproj src/BgStacks.Web/
COPY src/BggSdk/BggSdk.csproj src/BggSdk/
RUN dotnet restore src/BgStacks.Web/BgStacks.Web.csproj
COPY src/BgStacks.Web/ src/BgStacks.Web/
COPY src/BggSdk/ src/BggSdk/
RUN dotnet publish src/BgStacks.Web/BgStacks.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-azurelinux3.0-distroless
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "BgStacks.Web.dll"]
