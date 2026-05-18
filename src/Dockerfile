FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

RUN apt-get update && apt-get install -y --no-install-recommends \
   binutils-aarch64-linux-gnu \
   clang \
   zlib1g-dev

WORKDIR /app

# Copy everything
COPY . ./
# Restore as distinct layers
RUN dotnet restore
# Build and publish a release
RUN dotnet publish -c Release -o /app/publish

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0

#USER app
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT [ "./FraudDetection" ]
