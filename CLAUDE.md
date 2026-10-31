# CLAUDE.md — NetBoxPlus-Backend (v2)

NetBoxPlus — Netflix benzeri video/abonelik platformu. Bu repo tüm backend'in monorepo'su: .NET 10
mikroservisler, servis başına Clean Architecture, ortak kod `src/BuildingBlocks/` altında.
Her geliştirmeden sonra bu dosya ve ilgili doküman güncellenir.

- Kararlar ve gerekçeleri: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
- Aktif faz ve iş dökümü: [docs/PHASE-1.md](docs/PHASE-1.md) (Faz 0 bitti: [docs/PHASE-0.md](docs/PHASE-0.md))

Bu iki doküman **sadece lokalde** durur, repoya commit edilmez (`.gitignore`'da `/docs/*.md`).
GitHub'da yokturlar; commit'lenen hiçbir dosya (README dahil) onlara link vermez.

Kod yazmadan önce ilgili ARCHITECTURE bölümünü oku. Orada karara bağlanmış bir konuyu yeniden
tartışmaya açma; kararın yanlış olduğunu düşünüyorsan gerekçesiyle söyle, karar değişirse önce
dokümanı güncelle.

## Branch'ler

| Ref | Ne |
|---|---|
| `v2` | **Aktif geliştirme.** Yeni kod burada. |
| `master` | Varsayılan branch; v2 olgunlaşıp merge edilene kadar v1'i gösterir. Doğrudan commit yok. |
| `v1-graduation` (tag) | Eski bitirme projesinin (.NET 8) son hali. Sadece referans. |
| `dev`, `movie` | v1 döneminin yarım kalmış branch'leri. Dokunulmaz. |

Frontend ayrı repoda: `NetBoxPlus-Frontend` (web + admin).

## Yapı

```
src/BuildingBlocks/   Shared.Domain, Shared.Application, Shared.Infrastructure.{API,Persistence,Messaging}
src/Services/<S>/     <S>.Domain, .Application, .Infrastructure, .API, .Contracts
src/Gateway/          YARP
tests/                src/ ile aynı düzen
deploy/               compose, observability konfigürasyonu
docs/                 ARCHITECTURE.md, PHASE-*.md, openapi/
```

## Komutlar

```bash
dotnet build NetBoxPlus.slnx
```

```bash
dotnet test --solution NetBoxPlus.slnx
```

```bash
dotnet test --project tests/BuildingBlocks/Shared.Domain.Tests
```

Testler **Microsoft.Testing.Platform** ile çalışır (`global.json` → `test.runner`). Hiç test
çalışmayan bir proje çıkış kodu 8 ile başarısız sayılır — bu bilerek açık.

Persistence ve Messaging integration testleri **Testcontainers** ile gerçek Postgres/RabbitMQ'ya
karşı çalışır ve **Docker** ister. Docker yoksa hata vermez, "atlandı" olarak raporlanır — atlanan
bir test doğrulanmış sayılmaz. CI'da `NETBOXPLUS_REQUIRE_DOCKER=true` set edilir ve Docker
yokluğu testi **kırar**. Container başlatan her fixture bunu `DockerAvailability.CanSkip(exception)`
ile yapar (`tests/TestSupport/DockerAvailability.cs`, csproj'a link'lenir). Test image'ları
`deploy/compose.yaml` ile aynı sürümde tutulur.

**CI** (`.github/workflows/ci.yml`, `v2` ve `master`'a push ve PR'larda): Release build (uyarı =
hata) → `docs/openapi` build çıktısıyla aynı değilse kırılır → tüm testler (Docker zorunlu) →
paralel olarak her Dockerfile'ın image build'i (push yok). API değiştiyse build'i lokalde çalıştırıp
`docs/openapi/*.json`'ı da commit'le. Action'lar commit SHA'sıyla sabit; Dependabot (actions + NuGet)
haftalık PR'ları `v2`'ye açar.

Lokal bağımlılıklar (Postgres, RabbitMQ, Valkey, Aspire dashboard; `-Search` ile Elasticsearch):

```powershell
./deploy/dev.ps1 up
```

- Credential'lar sadece `deploy/.env`'de (git'te yok); ilk `up` rastgele parolalarla üretir.
- Postgres'te servis başına database ve aynı adlı, sadece kendi database'ine bağlanabilen kullanıcı.
- Tüm portlar `127.0.0.1`'e bağlı. Image'lar sürüm etiketiyle sabit, `latest` yok.
- PowerShell script'lerinde regex için **`-cmatch`** kullan: `-match` kültüre göre büyük/küçük harf
  eşler ve `tr-TR`'de `I` harfi `[A-Z]` ile eşleşmez.
- `dev.ps1`'i `2>&1` ile çağırma: Windows PowerShell docker'ın stderr'deki ilerleme çıktısını
  hataya çevirir ve script durur.
- `NETBOXPLUS_DATABASES`'e eklenen database sadece boş volume'da oluşur: ekledikten sonra
  `./deploy/dev.ps1 reset -Force` (lokal veri silinir), sonra `up` ve `secrets`.

Bir servisi lokalde çalıştırmak (örnek: Sample):

```powershell
./deploy/dev.ps1 secrets
dotnet run --project src/Services/Sample/Sample.Migrator
dotnet run --project src/Services/Sample/Sample.API --launch-profile Sample.API
```

Feed ve Auth da aynı şekilde (`<S>.Migrator`, `<S>.API`). Gateway (`http://localhost:5100`;
Development'ta Sample'a `localhost:5101`, Feed'e `localhost:5102` üzerinden gider):

```powershell
dotnet run --project src/Gateway/Gateway --launch-profile Gateway
```

`secrets`, `deploy/.env`'deki değerleri servisin User Secrets'ına (`netboxplus-<servis>`) yazar.
Auth için bir kez token imza anahtarı da üretir (`deploy/tools/NewSigningKey.cs`). Auth'un
Migrator'ı migration'dan sonra ilk admin'i seed eder (`Auth:Seed:AdminEmail`/`AdminPassword`;
lokalde `deploy/.env`'deki `AUTH_ADMIN_EMAIL`/`AUTH_ADMIN_PASSWORD`, `secrets` yazar). Servisler
token'ı Auth'un JWKS'i ile doğruladığı için korumalı bir endpoint'i denerken Auth da çalışmalı.
Migration eklemek: `dotnet tool restore` ve
`dotnet ef migrations add <Ad> --project src/Services/<S>/<S>.Infrastructure --output-dir Persistence/Migrations`.

## Yeni servis — referans: `src/Services/Sample`

Yeni bir servis Sample'ın yapısını birebir izler:

- Projeler `<S>.Domain`, `.Application`, `.Infrastructure`, `.API`, `.Contracts`, `.Migrator`;
  `.Contracts` sadece servis event yayınlıyorsa açılır. Testler `tests/Services/<S>/`. API ve
  Migrator aynı `UserSecretsId`'yi kullanır: `netboxplus-<servis>`.
- Domain: aggregate `AggregateRoot`'tan türer; value object'ler `Create` ile `Result` döner;
  hatalar `<Aggregate>Errors` static sınıfında; repository arayüzü domain'de.
- Application: use case başına bir dosya (`Notes/CreateNote/CreateNote.cs`: command + validator +
  handler); handler'lar `internal sealed`. Query'ler servisin okuma arayüzünden
  (`I<S>ReadModel`) projection yapar. Integration event'ler bir domain event handler'ından yayınlanır.
- Infrastructure: `<S>DbContext : ServiceDbContext, I<S>ReadModel`, `ConnectionStrings:Database`,
  configuration'lar `IEntityTypeConfiguration`, tek değerli value object'ler `ComplexProperty`
  (üzerinde index veya arama gerekiyorsa `HasConversion`, ör. Auth'taki `EmailAddress`),
  soft delete için `HasQueryFilter`, design-time factory.
- API: `Program.cs` Sample'daki sırayla; endpoint'ler `MapGroup` ile, `[FromServices] IDispatcher`,
  `Produces`/`ProducesProblem` metadata'sı (OpenAPI'den TypeScript client üretilecek). OpenAPI
  dokümanı build'de `docs/openapi/<servis>.json`'a yazılır ve commit'lenir; build sırasında
  altyapı kaydı `BuildTimeOpenApi.IsGeneratingDocument` ile atlanır.
  `appsettings.Development.json`: `Authentication:Schemes:Bearer:Authority` = `http://localhost:5103`
  ve `RequireHttpsMetadata: false`; Authority yoksa servis başlamaz. Test fixture'ı token'ı kendi
  ES256 anahtarıyla ve `typ: at+jwt` ile imzalar, anahtarı `PostConfigure<JwtBearerOptions>` ile
  verir (`SampleApiFixture`).
- Dockerfile Sample'daki gibi (chiseled, non-root). Yeni servis `deploy/dev.ps1`'deki `$services`
  listesine, `deploy/compose.yaml`'daki `NETBOXPLUS_DATABASES`'e, `tests/Architecture.Tests`
  csproj'una ve `.github/workflows/ci.yml`'deki `image` matrix'ine eklenir.
- Gateway'e iki route eklenir (`src/Gateway/Gateway/appsettings.json`): `<s>-public`
  (`/<s>/{**remainder}`, `PathRemovePrefix: /<s>`, `RouteGroup: public`) ve, admin endpoint'i
  varsa, `<s>-admin` (`/admin/<s>/{**remainder}`, `PathPattern: /admin/{**remainder}`,
  `RouteGroup: admin`); cluster adresi base'de compose adı, `appsettings.Development.json`'da
  `localhost` portu. Portlar: gateway 5100, Sample 5101, Feed 5102, Auth 5103; sıradaki servis 5104.
- Admin yazma command'ları `IAuditedCommand` implement eder (`AuditTargetId` explicit, JSON'a
  girmesin) ve secret taşımaz; liste endpoint'leri `Shared.Application.Paging` (`page`,
  `pageSize`, en fazla 100).
- Servisin permission'ları önce `src/Services/Auth/Auth.Domain/Roles/Permissions.cs`'te kendi
  iç içe sınıfına eklenir (`<servis>:<kaynak>:<eylem>`, küçük harf); servis aynı string'i
  `RequirePermission` ile kontrol eder. Katalogda olmayan permission bir role atanamaz.

**Başka bir servisin event'ini tüketmek — referans: `src/Services/Feed`**

- `<S>.Application` diğer servisin sadece `*.Contracts` projesini referans alır.
- `IIntegrationEventHandler<TEvent>` event'i bir command'a çevirip `dispatcher.Send` ile gönderir;
  kayıt, validation ve inbox kaydı aynı pipeline/transaction'da olur. Ayrıca kayıt yok — `AddCqrs`
  handler'ı bulur, messaging de bu event'e abone olur.
- Handler'ın tam tip adı inbox anahtarıdır: işlenmiş mesajı olan bir handler'ı yeniden adlandırma
  veya taşıma, yoksa o mesajlar tekrar işlenir.
- Command handler aynı kaydı ikinci kez görürse başarılı döner (idempotent); DB'de unique index.

## Gateway — `src/Gateway/Gateway`

- İş mantığı yok; sadece `Shared.Infrastructure.API`'yi referans alır.
- Route'un CORS ve rate limiter policy'si **grubundan** gelir (`RouteGroupConfigFilter`); route'a
  `CorsPolicy`/`RateLimiterPolicy` yazılmaz. Kurala uymayan route'la gateway başlamaz.
- Public route'tan servisin `/admin` yollarına erişim 404'tür; servisin döndüğü `Location`
  gateway yoluna çevrilir. Servisler gateway'i bilmez, kendi yollarını döner.
- İstemcinin trace header'ları devam ettirilmez; trace gateway'de başlar.

## Çalışma kuralları

- **Dil:** kullanıcıyla sohbet ve md dosyaları Türkçe. Bunun dışındaki **her şey İngilizce**: kod,
  identifier'lar, yorumlar, XML doc, log mesajları, hata mesajları ve kodları, test verisi,
  konfigürasyon (yorumlar dahil), script çıktıları, kullanıcı adları ve diğer değerler, commit
  mesajları. PowerShell script'leri ayrıca **sadece ASCII** (Windows PowerShell 5.1 BOM'suz UTF-8'i
  ANSI okur).
- Kullanıcı tasarımı kod yazmadan önce netleştirmek istiyor. Büyük bir değişiklikten önce yaklaşımı
  anlat, onay al.
- PHASE dosyasındaki bir kalem bittiğinde `[x]` olarak işaretle; yeni bir uygulama kararı alındıysa
  ARCHITECTURE'a taşı.
- Yeni bir öneri kararla çelişiyorsa sessizce uygulama, açıkça sor.
- **Commit'lerde Claude izi olmaz:** `Co-Authored-By` satırı eklenmez, mesajda Claude/AI geçmez.
  Commit mesajı sadece değişikliği anlatır.
- **Commit'lenen md dosyaları sadece `CLAUDE.md` ve `README.md`.** Tasarım, faz, not gibi diğer
  tüm md dosyaları lokal kalır; yeni bir md dosyası açılırsa ignore edildiğinden emin ol.
- **GitHub'da hiçbir şey silinmez** (repo, branch, tag, release, paket). Silme gerekiyorsa kullanıcıya
  bırakılır. Force push yok.
- Repo public. Secret'lar repoya girmez: User Secrets / environment variable. v1 geçmişindeki eski
  değerler hiçbir yerde kullanılmaz.
- Lisans: MIT.

## Temel kurallar (özet — ayrıntı ARCHITECTURE'da)

- **SDK:** stable .NET 10, `global.json` (`10.0.403`, `latestFeature`, prerelease yok).
- **Paketler:** Central Package Management (`Directory.Packages.props`); csproj'larda `Version`
  attribute'u yok. Uyarılar hata (`TreatWarningsAsErrors`); uyarıyı susturma, düzelt.
- **Referans kuralları:**
  - Servis içinde `Domain` ← `Application` ← `Infrastructure` ← `API`.
  - BuildingBlocks içinde `Domain` ← `Application` ← `Infrastructure.*`; `Infrastructure.*`
    projeleri birbirini referans almaz (köprü `Shared.Application`'daki arayüzler).
  - Bir servis başka bir servisten **sadece `*.Contracts`** projesini referans alabilir.
  - **`Shared.Domain`'e hiçbir `PackageReference` eklenmez.**
- **Shared'a girmeyenler:** generic repository, event/contract modelleri, servise özel hatalar.
- **Hatalar:** handler'lar `Result` döner, exception akış kontrolü için kullanılmaz. HTTP sınırında
  RFC 9457 ProblemDetails.
- **CQRS:** `IDispatcher.Send` / `Query`; kayıt `services.AddCqrs(assembly)`. Handler'lar
  **`SaveChanges` çağırmaz** — başarılı command'ı `TransactionBehavior` kaydeder. Bir request'in
  tam olarak bir handler'ı olur.
  - **İstisna — başarısızlıkta da kalması gereken güvenlik yazmaları:** repository'de
    `ExecuteUpdate` ile **anında ve atomik SQL** (ör. `FailedSignInCount + 1`, "UsedAt boşsa
    doldur, etkilenen satır = 1 mi"). Sadece command başarısız olsa bile kalıcı olması gereken veya
    eş zamanlı isteklerin yarıştığı yazmalar için; metodun XML doc'unda gerekçesi yazar. Örnekler:
    `IUserRepository.RecordFailedSignInAsync`, `IRefreshTokenRepository.TryMarkUsedAsync`.
- **Validation:** `IValidator<T>` senkron ve I/O yapmaz (zorunlu alan, uzunluk, format). Veritabanı
  gerektiren kurallar handler'da. Validator hatası exception değil `ValidationError`.
- **Contract'lar:** `*.Contracts` içinde düz record; işaret arayüzü yok, bağımlılık yok, tipler
  yeniden adlandırılmaz (mesaj tipi adı tip adından gelir). Namespace **`<Servis>.Contracts`**
  olmak zorunda — RabbitMQ exchange adı buradan türetiliyor.
- **Persistence:** servisin DbContext'i `ServiceDbContext`'ten türer, modelini `ConfigureModel`'da
  kurar (`OnModelCreating` sealed). Kayıt: `services.AddPersistence<TContext>(connectionString)`.
  Event yayınlamak için `IIntegrationEventPublisher` — outbox'a yazar, doğrudan kuyruğa değil.
- **Messaging:** `services.AddRabbitMqMessaging()`, ayarlar `Messaging` bölümünde. Integration
  event handler'ları `IIntegrationEventHandler<T>` implement eder ve `Task<Result>` döner: kalıcı
  başarısızlık `Result.Failure`, geçici sorun exception (retry → DLQ). Her mesaj bir handler'a en
  fazla bir kez ulaşır (inbox).
- **Zaman:** `DateTime.UtcNow` doğrudan çağrılmaz, `TimeProvider` kullanılır.
- **ID'ler:** düz `Guid`, `Guid.CreateVersion7()`.
- **API:** Minimal API. Her uygulama `builder.AddServiceDefaults()` → `app.UseServiceDefaults()` →
  (auth) → endpoint'ler → `app.MapDefaultEndpoints()`. Endpoint'ler `result.ToHttpResult()` döner;
  hatalar RFC 9457 problem details. Yetki `RequirePermission("servis:kaynak:eylem")`.
- **Loglama:** Serilog yok; .NET `ILogger` + OpenTelemetry. `[LoggerMessage]` ile structured log,
  string interpolation yok; token/şifre/kart/e-posta loglanmaz. Hassas parametreler
  `[PersonalData]` / `[SecretData]` ile işaretlenir (projede `Microsoft.Extensions.Telemetry.Abstractions`
  doğrudan referanslı olmalı).
- **Auth endpoint'leri:** `/register`, `/sign-in`, `/admin/sign-in`, `/refresh`, `/sign-out`, `/me`,
  `/me/password`. Giriş hatası her durumda `User.InvalidCredentials` (hesabın varlığı ve kilidi
  belli olmaz); bilinmeyen e-postada da bir hash doğrulanır. Tekrar kullanılan refresh token ailenin
  tamamını iptal eder.
- **Admin (Auth):** `/admin/users`, `/admin/roles`, `/admin/permissions`. Bir rolü oluşturan,
  değiştiren, silen, atayan veya kaldıran o rolün permission'larının hepsine sahip olmalı;
  `Administrator` sistem rolüdür (tüm permission'lar, API'den değişmez, seed katalogla eşitler).
- **Audit:** `IAuditedCommand` işaretli başarılı command'lar aynı transaction'da
  `AuditLogEntries`'e yazılır (`AuditBehavior`, tablo `ServiceDbContext`'te).
- **JWT:** ES256, `typ: at+jwt`, 15 dk; servisler Auth'un JWKS'ini `Authority` üzerinden bulur,
  başka algoritma veya tip kabul edilmez. Claim adları olduğu gibi (`sub`, `role`, `permission`).
  İmza anahtarları `Auth:Tokens` altında, asla loglanmaz; rotasyon adımları ARCHITECTURE §12.3.1.
- **Lisans kontrolü:** MediatR, AutoMapper, MassTransit v9, FluentAssertions v8 gibi ticari lisansa
  geçmiş paketler kullanılmaz. Yeni bir paket eklemeden önce lisansını kontrol et.
