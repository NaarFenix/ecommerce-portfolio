FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY EcommercePortfolio.slnx ./
COPY src/EcommercePortfolio.Domain/*.csproj ./src/EcommercePortfolio.Domain/
COPY src/EcommercePortfolio.Data/*.csproj ./src/EcommercePortfolio.Data/
COPY src/EcommercePortfolio.Infrastructure/*.csproj ./src/EcommercePortfolio.Infrastructure/
COPY src/EcommercePortfolio.Api/*.csproj ./src/EcommercePortfolio.Api/
RUN dotnet restore src/EcommercePortfolio.Api/EcommercePortfolio.Api.csproj

COPY . .
RUN dotnet publish src/EcommercePortfolio.Api/EcommercePortfolio.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
 && apt-get install -y --no-install-recommends libfontconfig1 libfreetype6 \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "EcommercePortfolio.Api.dll"]