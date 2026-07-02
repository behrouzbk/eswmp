FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["src/Eswmp.Shared/Eswmp.Shared.csproj", "src/Eswmp.Shared/"]
COPY ["src/Eswmp.Core/Eswmp.Core.csproj", "src/Eswmp.Core/"]
RUN dotnet restore "src/Eswmp.Core/Eswmp.Core.csproj"
COPY . .
WORKDIR "/src/src/Eswmp.Core"
RUN dotnet build "Eswmp.Core.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Eswmp.Core.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Eswmp.Core.dll"]
