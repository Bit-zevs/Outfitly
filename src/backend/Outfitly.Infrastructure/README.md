# Infrastructure

Слой хранения на EF Core 10 и PostgreSQL (Npgsql). Domain не зависит от EF Core.
Локальный `Directory.Packages.props` импортирует корневой файл.
Тесты находятся в `tests/backend/Outfitly.Tests/Infrastructure` и используют xUnit.

## Модель хранения

- `Users` — пользователи.
- `Accounts` — учётные данные, признак подтверждения почты, SHA-256 токена,
  срок действия и время выдачи; хеш имеет уникальный индекс и служит concurrency token.
- `LoginSessions` — сессии аккаунтов с датой истечения и отзывом.
- `WardrobeItems` — вещи, публикация, внешний URL либо ключ фото `PhotoStorageKey`;
  ключ также служит concurrency token для замены фото и удаления вещи.
- `Outfits` — образы, описание, состояние публикации и concurrency token `Revision`.
- `OutfitItems` — состав образа. Составной ключ запрещает дубликаты; внешние ключи
  через `OwnerId` разрешают связывать только вещи и образы одного владельца.
- `ShareLinks` — ссылки с уникальным 64-символьным токеном, типом цели и состоянием.
  Полиморфный `TargetId` не имеет внешнего ключа: деактивацией ссылок управляет
  существующий `WardrobeService`. Деактивированные ссылки сохраняются после удаления цели.

Идентификаторы задаются доменом. Enum хранятся строками. Необязательные поля допускают
`NULL`. Удаление владельца с вещами/образами запрещено. Удаление образа удаляет только
его связи; удалять вещь следует через use case, который сначала обновляет образы и ссылки.

## Подключение

Composition root в API использует:

```csharp
services.AddOutfitlyInfrastructure(connectionString);
services.AddScoped<WardrobeService>();
```

`OutfitlyDbContext` и `IWardrobeRepository` имеют scoped lifetime. Контекст не потокобезопасен.
Пользователь должен быть сохранён в `context.Users` до сохранения принадлежащих ему вещей
и образов. Строка подключения передаётся извне; секреты в репозитории не хранятся.

`EfWardrobeRepository` реализует текущий синхронный контракт Application. Чтение загружает
отслеживаемые сущности, включая состав образов, и возвращает снимок с учётом несохранённых
добавлений/удалений. Отдельный `EfItemCatalogQuery` выполняет асинхронные запросы
каталога с фильтрацией, подсчётом и пагинацией без отслеживания сущностей.

Каждый use case завершается **одним** `await context.SaveChangesAsync(cancellationToken)`
на том же scoped контексте. Так удаление вещи, изменение состава образов и деактивация
ссылок сохраняются в одной транзакции. Репозиторий самостоятельно не вызывает SaveChanges:
сервис также меняет сущности без вызовов Add/Remove. При ошибке сохранения следует завершить
scope и освободить контекст; откат транзакции не откатывает состояние объектов в памяти.

Каждая команда над существующим образом должна вызвать `MarkOutfitChanged` после
чтения в том же контексте, даже если меняются только связи или ссылки. EF меняет
`Revision` в одной транзакции с остальными записями и проверяет исходную версию
при UPDATE/DELETE. Конфликт переводится в Application-исключение
`WardrobeConcurrencyException`; повторять команду можно только в новом scope.
InMemoryWardrobeRepository — тестовый адаптер с общими объектами, а не замена
защите независимых снимков EF. Прямой SQL обходит механизм координации команд.

API подключает EF-репозиторий и сохраняет успешные команды через `IUnitOfWork`,
зарегистрированный как тот же scoped `OutfitlyDbContext`. Контекст не попадает
в Application; контракт сохранения не зависит от EF.

## Миграции

Миграция `EmailConfirmation` добавляет поля подтверждения почты и отмечает все существующие
аккаунты подтверждёнными. Для последующих INSERT значение `EmailConfirmed` по умолчанию — false.
Применяйте миграцию явно перед запуском обновлённого API; startup схему не меняет.

Миграции и snapshot находятся в `Persistence/Migrations`. `OutfitConcurrency`
добавляет версию существующим образам; `AccountsAndSessions` добавляет учётные данные,
уникальный индекс нормализованного email и сессии. `AddItemPhotos` добавляет nullable
`PhotoStorageKey`; существующие внешние URL сохраняются. Существующие Users сохраняются
без автоматически созданных аккаунтов. Перед запуском обновлённого API примените миграции.
Команды выполняются
из корня репозитория. Используйте `dotnet-ef` версии 10.0.12; при необходимости установите
его локально в игнорируемую папку:

```powershell
dotnet tool install dotnet-ef --version 10.0.12 --tool-path src/backend/Outfitly.Infrastructure/obj/tools
$ef = './src/backend/Outfitly.Infrastructure/obj/tools/dotnet-ef.exe'
# Задайте OUTFITLY_CONNECTION_STRING через окружение перед запуском EF tools.
& $ef migrations add MigrationName --project src/backend/Outfitly.Infrastructure --startup-project src/backend/Outfitly.Infrastructure --output-dir Persistence/Migrations
& $ef migrations script --idempotent --project src/backend/Outfitly.Infrastructure --startup-project src/backend/Outfitly.Infrastructure
& $ef database update --project src/backend/Outfitly.Infrastructure --startup-project src/backend/Outfitly.Infrastructure
```

Фабрика design-time контекста читает `OUTFITLY_CONNECTION_STRING`. Контекст не создаёт
базу и не применяет миграции автоматически. Пример формата без пароля:
`Host=localhost;Port=5432;Database=outfitly;Username=outfitly`.

## Проверки

```powershell
dotnet build Outfitly.sln
dotnet test Outfitly.sln
```

Интеграционные тесты используют SQLite in-memory для реальных реляционных операций: повторная
загрузка всех сущностей, сохранение обновлений, связей и токенов, удаление, отзыв ссылок,
ограничения внешних ключей/уникальности и откат транзакции. Отдельно проверяется
актуальность PostgreSQL snapshot без подключения к БД.
Для выполнения миграции и сценариев хранения на PostgreSQL задайте
`OUTFITLY_TEST_CONNECTION_STRING`, указывающую на отдельную тестовую базу.
Каждый тест создаёт и удаляет собственную схему `outfitly_test_<guid>`; нужны права
на создание схем. Без этой переменной PostgreSQL-тесты явно пропускаются.
Подробнее: [стратегия тестирования](../../../tests/backend/Outfitly.Tests/README.md).

`PasswordReset` добавляет отдельные nullable поля токена/срока/времени выдачи сброса,
уникальный индекс хеша и SessionVersion в Accounts/LoginSessions. Нулевая версия
сохраняет работоспособность существующих сессий. Применение — теми же явными командами выше.
