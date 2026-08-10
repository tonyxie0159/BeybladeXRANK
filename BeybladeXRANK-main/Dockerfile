FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/BeybladeRecordSystem/BeybladeRecordSystem.csproj", "src/BeybladeRecordSystem/"]
RUN dotnet restore "src/BeybladeRecordSystem/BeybladeRecordSystem.csproj"
COPY . .
WORKDIR "/src/src/BeybladeRecordSystem"
RUN dotnet publish "BeybladeRecordSystem.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BeybladeRecordSystem.dll"]
