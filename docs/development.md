# Разработка и запуск

## Требования

- .NET SDK согласно [global.json](../global.json), целевая платформа — .NET 10.
- PostgreSQL для API; SQLite используется только в тестах.
- Node.js/npm для существующих скриптов frontend.

Команды backend выполняются из корня репозитория:

```powershell
dotnet build Outfitly.sln
dotnet test Outfitly.sln
```

## Конфигурация API и миграции

API читает `ConnectionStrings:Outfitly`; через окружение это
`ConnectionStrings__Outfitly`. EF design-time фабрика отдельно читает
`OUTFITLY_CONNECTION_STRING`. Задайте обе переменные для работы с одной базой.
Пароль хранится вне репозитория. Формат без пароля:
`Host=localhost;Port=5432;Database=outfitly;Username=outfitly`.

Примените миграции по [инструкции Infrastructure](../src/backend/Outfitly.Infrastructure/README.md),
затем запустите API:

```powershell
dotnet run --project src/backend/Outfitly.Api
```

Строка подключения обязательна; схема не создаётся автоматически.
GET /health проверяет HTTP-приложение, а не доступность БД.
Личные операции пока отвечают 401: аутентификация — отдельный шаг.
Публичные списки и ссылки требуют доступной БД с миграциями.

## PostgreSQL-тесты

Задайте `OUTFITLY_TEST_CONNECTION_STRING` на отдельную тестовую базу:

```powershell
dotnet test Outfitly.sln --filter "FullyQualifiedName~PostgreSqlTests"
```

Нужны права создания схем. Каждый тест создаёт схему `outfitly_test_<guid>`
и удаляет её после завершения. Без переменной эти тесты имеют статус Skip;
при заданной, но недоступной БД тесты падают. Обычные SQLite-проверки
не подтверждают выполнение PostgreSQL-миграций.
Детали — в [стратегии тестирования](../tests/backend/Outfitly.Tests/README.md).

## Frontend

Из src/frontend команды запускаются отдельно:

```powershell
npm run dev
npm run build
npm test
```

Dev запускает сервер, build создаёт dist, test проверяет HTTP-границу dev server.
Для тестов порт 5173 должен быть свободен; предварительно остановите dev server.

## Поддержка архитектуры

Общие настройки компилятора находятся в Directory.Build.props.
Версии пакетов задаются через PackageVersion в Directory.Packages.props;
локальный файл Infrastructure импортирует корневой. PackageReference без Version
размещается только в проекте, использующем пакет.

При изменении слоя, доступа или границы сохранения обновляйте
[архитектуру](architecture.md). Маршруты документируются в API,
схема — в маппингах и миграциях, важные сценарии — в тестах.
