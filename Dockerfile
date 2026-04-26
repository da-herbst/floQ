FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/floQ.Domain/floQ.Domain.csproj ./floQ.Domain/
COPY src/floQ.Web/floQ.Web.csproj ./floQ.Web/
RUN dotnet restore floQ.Web/floQ.Web.csproj
COPY src/floQ.Domain/ ./floQ.Domain/
COPY src/floQ.Web/ ./floQ.Web/
WORKDIR /src/floQ.Web
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8083
ENV ASPNETCORE_URLS=http://+:8083
ENTRYPOINT ["dotnet", "floQ.Web.dll"]
