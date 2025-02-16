# ----------------------------
# STAGE 1: Build the .NET app
# ----------------------------
    FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
    WORKDIR /app
    
    # Copy everything into the container
    COPY . .
    
    # Restore, build, and publish (release mode)
    RUN dotnet restore
    RUN dotnet publish -c Release -o out
    
    # ----------------------------
    # STAGE 2: Create runtime image
    # ----------------------------
    FROM mcr.microsoft.com/dotnet/runtime:8.0
    WORKDIR /app
    
    # Copy output from build stage
    COPY --from=build /app/out .
    
    ENTRYPOINT ["dotnet", "CloudSyncConsumer.dll"]
    