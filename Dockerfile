# ASP.NET Core on .NET 10 (TFM net10.0).
# Pass the database connection at runtime. Do not bake secrets into the image.
#   docker build -t carpark-occupancy-api .
#   docker run --rm -p 8080:8080 \
#     -e ConnectionStrings__CarParkDb="Server=...;Database=...;User Id=...;Password=...;Encrypt=True" \
#     carpark-occupancy-api

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/CarParkOccupancy.Api/CarParkOccupancy.Api.csproj src/CarParkOccupancy.Api/
RUN dotnet restore src/CarParkOccupancy.Api/CarParkOccupancy.Api.csproj
COPY src/CarParkOccupancy.Api/ src/CarParkOccupancy.Api/
RUN dotnet publish src/CarParkOccupancy.Api/CarParkOccupancy.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CarParkOccupancy.Api.dll"]
