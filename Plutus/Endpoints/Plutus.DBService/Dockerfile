#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["NuGet.config", "."]
COPY ["Endpoints/Plutus.DBService/Plutus.DBService.csproj", "Endpoints/Plutus.DBService/"]
COPY ["Commons/Plutus.Repository/Plutus.Repository.csproj", "Commons/Plutus.Repository/"]
COPY ["Commons/Plutus.Contracts/Plutus.Contracts.csproj", "Commons/Plutus.Contracts/"]
COPY ["Commons/Plutus.Entities/Plutus.Entities.csproj", "Commons/Plutus.Entities/"]
COPY ["Commons/Plutus.Reports/Plutus.Reports.csproj", "Commons/Plutus.Reports/"]
COPY ["Commons/Plutus.Authentication/Plutus.Authentication.csproj", "Commons/Plutus.Authentication/"]
RUN dotnet restore "Endpoints/Plutus.DBService/Plutus.DBService.csproj"
COPY . .
WORKDIR "/src/Endpoints/Plutus.DBService"
RUN dotnet build "Plutus.DBService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Plutus.DBService.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Plutus.DBService.dll"]