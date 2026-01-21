# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY *.sln .
COPY src/KubeMind.Brain.Api/*.csproj src/KubeMind.Brain.Api/
COPY src/KubeMind.Brain.Application/*.csproj src/KubeMind.Brain.Application/
COPY src/KubeMind.Brain.Core/*.csproj src/KubeMind.Brain.Core/
COPY src/KubeMind.Brain.Infrastructure/*.csproj src/KubeMind.Brain.Infrastructure/
# Add tests project as well for restore
COPY tests/KubeMind.Brain.Tests/*.csproj tests/KubeMind.Brain.Tests/

# Restore dependencies
RUN dotnet restore "KubeMind.Brain.slnx"

# Copy the rest of the source code
COPY . .
WORKDIR "/src/src/KubeMind.Brain.Api"

# Build the application
RUN dotnet build "KubeMind.Brain.Api.csproj" -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish "KubeMind.Brain.Api.csproj" -c Release -o /app/publish

# Stage 3: Final image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# The command to run the application
ENTRYPOINT ["dotnet", "KubeMind.Brain.Api.dll"]
