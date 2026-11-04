FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src
COPY Prediction.csproj .
RUN dotnet restore Prediction.csproj -r linux-x64
COPY . .
RUN dotnet publish Prediction.csproj -c Release -r linux-x64 --no-restore --self-contained false -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
WORKDIR /app
COPY --from=build /publish .
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN ./.playwright/node/linux-x64/node ./.playwright/package/cli.js install --with-deps chromium
ENTRYPOINT ["dotnet", "Prediction.dll"]
