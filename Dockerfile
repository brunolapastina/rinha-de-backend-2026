FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

RUN apt-get update && apt-get install -y --no-install-recommends \
   binutils-aarch64-linux-gnu \
   clang \
   zlib1g-dev

WORKDIR /app

# Copy everything
COPY ./src ./
# Restore as distinct layers
RUN dotnet restore
# Build and publish a release
RUN dotnet publish -c Release -o /app/publish

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0

#USER app
WORKDIR /app
COPY --from=build /app/publish .

COPY ./resources/mcc_risk.json /resources/mcc_risk.json
COPY ./resources/normalization.json /resources/normalization.json
COPY ./resources/references.json /resources/references.json

ENTRYPOINT [ "./FraudDetection" ]
