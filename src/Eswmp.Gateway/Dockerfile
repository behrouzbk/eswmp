FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["src/Eswmp.Gateway/Eswmp.Gateway.csproj", "src/Eswmp.Gateway/"]
RUN dotnet restore "src/Eswmp.Gateway/Eswmp.Gateway.csproj"
COPY . .
WORKDIR "/src/src/Eswmp.Gateway"
RUN dotnet build "Eswmp.Gateway.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Eswmp.Gateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Eswmp.Gateway.dll"]
