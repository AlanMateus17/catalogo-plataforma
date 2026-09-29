# Monta o sistema em duas etapas: compila com o SDK e roda só com o runtime (imagem menor).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS compilar
WORKDIR /src
COPY src/Plataforma/Plataforma.csproj src/Plataforma/
RUN dotnet restore src/Plataforma/Plataforma.csproj
COPY src/ src/
RUN dotnet publish src/Plataforma/Plataforma.csproj -c Release -o /publicado --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=compilar /publicado .
COPY site/ /app/site/
# Pasta das chaves que protegem os cookies: fica num volume para sobreviver às atualizações
RUN mkdir -p /app/chaves && chown app /app/chaves
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Site__Pasta=/app/site \
    DataProtection__Pasta=/app/chaves \
    Rede__AtrasDeProxy=true
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Plataforma.dll"]
