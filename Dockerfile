FROM mcr.microsoft.com/playwright/dotnet:v1.62.0-noble

WORKDIR /app

COPY . .

RUN dotnet restore Prediction.csproj
RUN dotnet publish Prediction.csproj -c Release -o /app/publish

WORKDIR /app/publish

ENTRYPOINT ["dotnet", "Prediction.dll"]
