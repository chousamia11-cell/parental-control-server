# ============ مرحلة البناء ============
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# نسخ ملف المشروع واستعادة الحزم
COPY ["Server.csproj", "./"]
RUN dotnet restore "Server.csproj"

# نسخ باقي الملفات وبناؤها
COPY . .
RUN dotnet publish "Server.csproj" -c Release -o /app/publish

# ============ مرحلة التشغيل ============
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Render يوفر متغير PORT تلقائياً
ENV ASPNETCORE_URLS=http://0.0.0.0:${PORT:-5000}
EXPOSE 5000

ENTRYPOINT ["dotnet", "Server.dll"]