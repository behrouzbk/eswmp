FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["src/Eswmp.Shared/Eswmp.Shared.csproj", "src/Eswmp.Shared/"]
COPY ["src/Eswmp.Assignment/Eswmp.Assignment.csproj", "src/Eswmp.Assignment/"]
RUN dotnet restore "src/Eswmp.Assignment/Eswmp.Assignment.csproj"
COPY . .
WORKDIR "/src/src/Eswmp.Assignment"
RUN dotnet build "Eswmp.Assignment.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Eswmp.Assignment.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Eswmp.Assignment.dll"]
