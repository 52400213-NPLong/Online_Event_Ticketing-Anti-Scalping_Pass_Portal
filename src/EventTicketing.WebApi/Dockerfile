# Stage 1: Build & Publish ASP.NET Core Web API (.NET 10)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build

# Copy project files first to leverage Docker layer caching during restore
COPY ["src/EventTicketing.Core/EventTicketing.Core.csproj", "src/EventTicketing.Core/"]
COPY ["src/EventTicketing.Infrastructure/EventTicketing.Infrastructure.csproj", "src/EventTicketing.Infrastructure/"]
COPY ["src/EventTicketing.WebApi/EventTicketing.WebApi.csproj", "src/EventTicketing.WebApi/"]
RUN dotnet restore "src/EventTicketing.WebApi/EventTicketing.WebApi.csproj"

# Copy full source code and publish Release build
COPY ["src/", "src/"]
RUN dotnet publish "src/EventTicketing.WebApi/EventTicketing.WebApi.csproj" -c Release -o /out /p:UseAppHost=false

# Stage 2: Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app/EventTicketing.WebApi

# Copy compiled WebApi output
COPY --from=build /out .

# Copy Frontend static assets & banner images preserving relative structure (../EventTicketing.WebClient/public & ../img)
COPY ["src/EventTicketing.WebClient/public/", "/app/EventTicketing.WebClient/public/"]
COPY ["src/img/", "/app/img/"]

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "EventTicketing.WebApi.dll"]
