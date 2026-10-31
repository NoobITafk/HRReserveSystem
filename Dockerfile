FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY HRReserveSystem.sln ./
COPY HRReserveSystem.csproj ./
COPY HRReserveSystem.Tests/HRReserveSystem.Tests.csproj HRReserveSystem.Tests/
RUN dotnet restore HRReserveSystem.csproj

COPY . ./
RUN dotnet publish HRReserveSystem.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

COPY --from=build /app/publish ./

RUN mkdir -p /app/DataProtectionKeys /app/EmailOutbox /app/wwwroot/uploads /app/App_Data/Storage \
    && chown -R $APP_UID:$APP_UID /app

USER $APP_UID

ENTRYPOINT ["dotnet", "HRReserveSystem.dll"]
