# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY Kube-Mind.sln .
COPY brain/src/KubeMind.Brain.Api/*.csproj brain/src/KubeMind.Brain.Api/
COPY brain/src/KubeMind.Brain.Application/*.csproj brain/src/KubeMind.Brain.Application/
COPY brain/src/KubeMind.Brain.Core/*.csproj brain/src/KubeMind.Brain.Core/
COPY brain/src/KubeMind.Brain.Infrastructure/*.csproj brain/src/KubeMind.Brain.Infrastructure/
COPY brain/src/KubeMind.Brain.Shared/*.csproj brain/src/KubeMind.Brain.Shared/
COPY brain/tests/KubeMind.Brain.Tests/*.csproj brain/tests/KubeMind.Brain.Tests/
COPY brain/scripts/SeedRedis/*.csproj brain/scripts/SeedRedis/


# Restore dependencies
RUN dotnet restore "Kube-Mind.sln"

# Copy the rest of the source code
COPY . .
WORKDIR "/src/brain/src/KubeMind.Brain.Api"

# Build the application
RUN dotnet build "KubeMind.Brain.Api.csproj" -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish "KubeMind.Brain.Api.csproj" -c Release -o /app/publish

# Stage 3: Final image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# The command to run the application
ENTRYPOINT ["dotnet", "KubeMind.Brain.Api.dll"]
