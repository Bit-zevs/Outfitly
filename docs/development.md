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
dotnet dev-certs https --trust
dotnet run --project src/backend/Outfitly.Api --launch-profile https
```

Строка подключения обязательна; схема не создаётся автоматически.
GET /health проверяет HTTP-приложение, а не доступность БД.
Личные операции доступны после регистрации и входа по [контракту API](../src/backend/Outfitly.Api/README.md).
Публичные списки и ссылки требуют доступной БД с миграциями.

Профиль запуска использует `https://localhost:7043`; `http://localhost:5043`
перенаправляет на HTTPS. Cookie и CSRF требуют HTTPS также при ручной проверке API.
Сначала GET `/api/auth/csrf`, затем POST регистрации/входа с cookie и заголовком
`X-CSRF-TOKEN`. После входа получите токен заново для личных команд.

### Ключи и размещение API

Cookie защищает ASP.NET Core Data Protection с именем приложения `Outfitly`.
Ключи не включаются в репозиторий. При локальном запуске используется стандартное
хранилище профиля пользователя; на Windows ключи защищаются DPAPI.
При размещении обеспечьте постоянное хранилище ключей и доступ только для учётной
записи приложения. В Linux-контейнере стандартный каталог `$HOME/.aspnet/DataProtection-Keys`
должен быть постоянным защищённым томом; шифрование тома/права задаются вне репозитория.
Потеря ключей делает существующие cookie недействительными.

Первая версия предназначена для одного экземпляра API с прямым HTTPS и frontend
того же origin. Для запуска без launch profile задайте HTTPS endpoint и сертификат
Kestrel через окружение/секреты размещения. Reverse proxy требует отдельной настройки
доверенных прокси, HTTPS scheme и клиентского IP для rate limiter; произвольным
заголовкам `X-Forwarded-*` текущая конфигурация не доверяет.

## PostgreSQL-тесты

Задайте `OUTFITLY_TEST_CONNECTION_STRING` на отдельную тестовую базу:

```powershell
dotnet test Outfitly.sln --filter "FullyQualifiedName~PostgreSql"
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

## CI

[GitHub Actions](../.github/workflows/ci.yml) запускается при push и для PR в master.
Проверка `Backend` собирает Release с SDK из global.json и запускает весь набор
тестов, включая миграции и ограничения настоящего PostgreSQL 17. База создаётся
на время job; строка подключения задана явно, поэтому PostgreSQL-тесты не пропускаются.
Проверка `Frontend` на Node.js 24 выполняет сборку и HTTP-тесты dev server.
Frontend пока не имеет сторонних зависимостей, поэтому npm install не требуется.

Для master в настройках GitHub должны быть обязательны PR и успешные проверки
`Backend` и `Frontend` с актуальной базовой веткой. Правило должно распространяться
на администраторов; force push и удаление ветки должны быть запрещены.
Защита ветки хранится в настройках GitHub, а не в workflow: при переносе репозитория
её нужно настроить отдельно. Имена обязательных jobs нельзя менять без обновления защиты.

## Поддержка архитектуры

Общие настройки компилятора находятся в Directory.Build.props.
Версии пакетов задаются через PackageVersion в Directory.Packages.props;
локальный файл Infrastructure импортирует корневой. PackageReference без Version
размещается только в проекте, использующем пакет.

При изменении слоя, доступа или границы сохранения обновляйте
[архитектуру](architecture.md). Маршруты документируются в API,
схема — в маппингах и миграциях, важные сценарии — в тестах.
