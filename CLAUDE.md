# CLAUDE.md — NetBoxPlus-Backend (v2)

NetBoxPlus — Netflix benzeri video/abonelik platformu. Bu repo tüm backend'in monorepo'su: .NET 10
mikroservisler, servis başına Clean Architecture, ortak kod `src/BuildingBlocks/` altında.
Her geliştirmeden sonra bu dosya ve ilgili doküman güncellenir.

- Kararlar ve gerekçeleri: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
- Aktif faz ve iş dökümü: [docs/PHASE-4.md](docs/PHASE-4.md) (bitenler: [docs/PHASE-0.md](docs/PHASE-0.md), [docs/PHASE-1.md](docs/PHASE-1.md), [docs/PHASE-2.md](docs/PHASE-2.md), [docs/PHASE-3.md](docs/PHASE-3.md))

Bu dokümanlar **sadece lokalde** durur, repoya commit edilmez (`.gitignore`'da `/docs/*.md`).
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
`docs/openapi/*.json`'ı da commit'le. Transcode testleri gerçek **ffmpeg** ister: `FFMPEG_DIRECTORY`
ya da PATH (`tests/TestSupport/FfmpegAvailability.cs`); yoksa lokalde atlanır, CI'da (ffmpeg `apt` ile
kurulur) kırar. Lokalde `winget install Gyan.FFmpeg`. Action'lar commit SHA'sıyla sabit; Dependabot (actions + NuGet)
haftalık PR'ları `v2`'ye açar.

Lokal bağımlılıklar (Postgres, RabbitMQ, Valkey, Azurite, Aspire dashboard; `-Search` ile Elasticsearch):

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

Bir servisi lokalde çalıştırmak (örnek: Auth):

```powershell
./deploy/dev.ps1 secrets
dotnet run --project src/Services/Auth/Auth.Migrator
dotnet run --project src/Services/Auth/Auth.API --launch-profile Auth.API
```

Catalog araması Elasticsearch ister: `./deploy/dev.ps1 up -Search`. Index'i veritabanından yeniden kurmak (kesintisiz):
`dotnet run --project src/Services/Catalog/Catalog.Migrator -- reindex`.

Billing ödemeleri `deploy/.env`'deki `STRIPE_SECRET_KEY` (test modu) ile çalışır. Stripe'ın gönderdiği webhook'lar için Stripe CLI;
CLI oturumu başka bir hesapta olabilir, `.env`'deki anahtarın hesabını dinlemek için `--api-key` ver ve
`stripe listen --api-key <anahtar> --print-secret` değerini `STRIPE_WEBHOOK_SECRET` olarak `.env`'ye yaz:
`stripe listen --api-key <anahtar> --forward-to localhost:5100/billing/webhooks/stripe`.

Streaming videoları transcode eden ayrı süreç (aynı database ve User Secrets; ffmpeg PATH'te veya
`Transcoder:FfmpegPath`/`FfprobePath`): `dotnet run --project src/Services/Streaming/Streaming.Transcoder`.
RabbitMQ'ya bağlanmaz; event'lerini Streaming.API'nin outbox işlemcisi gönderir, o da çalışmalı.

Başka servisin verisini kopyalayan bir tüketici sonradan açıldıysa, kaynak servisin Migrator'ı mevcut
durumu yeniden yayınlar (outbox'ı servisin API'si gönderir): `-- announce-profiles` (Auth),
`-- announce-subscriptions` (Billing), `-- announce-playable` (Catalog). Örnek:
`dotnet run --project src/Services/Auth/Auth.Migrator -- announce-profiles`.

Diğer servisler de aynı şekilde (`<S>.Migrator`, `<S>.API`). Gateway (`http://localhost:5100`;
Development'ta Auth'a `localhost:5103`, Catalog'a `localhost:5104`, Billing'e `localhost:5105`, Streaming'e `localhost:5106` üzerinden gider; tarayıcı oturumları için Valkey ve Auth çalışmalı):

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

## Yeni servis — referans: `src/Services/Auth`

Yeni bir servis Auth'un yapısını izler (Auth'a özel olanlar hariç: kendi token'ını üretip doğrulaması,
Migrator'daki seed):

- Projeler `<S>.Domain`, `.Application`, `.Infrastructure`, `.API`, `.Contracts`, `.Migrator`;
  `.Contracts` sadece servis event yayınlıyorsa açılır. Testler `tests/Services/<S>/`. API ve
  Migrator aynı `UserSecretsId`'yi kullanır: `netboxplus-<servis>`.
- Domain: aggregate `AggregateRoot`'tan türer; value object'ler `Create` ile `Result` döner;
  hatalar `<Aggregate>Errors` static sınıfında; repository arayüzü domain'de.
- Application: use case başına bir dosya (`Roles/CreateRole/CreateRole.cs`: command + validator +
  handler); handler'lar `internal sealed`. Query'ler servisin okuma arayüzünden
  (`I<S>ReadModel`) projection yapar. Integration event'ler bir domain event handler'ından yayınlanır.
- Infrastructure: `<S>DbContext : ServiceDbContext, I<S>ReadModel`, `ConnectionStrings:Database`,
  configuration'lar `IEntityTypeConfiguration`, tek değerli value object'ler `ComplexProperty`
  (üzerinde index veya arama gerekiyorsa `HasConversion`, ör. Auth'taki `EmailAddress`),
  soft delete için `HasQueryFilter`, design-time factory. Domain'in ürettiği id'lerde `ValueGeneratedNever`,
  string ile verilen gölge FK'da zorunluysa `IsRequired()` (varsayılan nullable).
- API: `Program.cs` Auth'taki sırayla; endpoint'ler `MapGroup` ile, `[FromServices] IDispatcher`,
  `Produces`/`ProducesProblem` metadata'sı (OpenAPI'den TypeScript client üretilecek). OpenAPI
  dokümanı build'de `docs/openapi/<servis>.json`'a yazılır ve commit'lenir; build sırasında
  altyapı kaydı `BuildTimeOpenApi.IsGeneratingDocument` ile atlanır.
  `appsettings.Development.json`: `Authentication:Schemes:Bearer:Authority` = `http://localhost:5103`
  ve `RequireHttpsMetadata: false`; Authority yoksa servis başlamaz. Test fixture'ı token'ı kendi
  ES256 anahtarıyla ve `typ: at+jwt` ile imzalar, anahtarı `PostConfigure<JwtBearerOptions>` ile
  verir (örnek: `tests/Services/Auth/Auth.IntegrationTests/Tokens/ResourceServer.cs`).
- Dockerfile Auth'takiler gibi (chiseled, non-root). Yeni servis `deploy/dev.ps1`'deki `$services`
  listesine, `deploy/compose.yaml`'daki `NETBOXPLUS_DATABASES`'e, `tests/Architecture.Tests`
  csproj'una ve `.github/workflows/ci.yml`'deki `image` matrix'ine eklenir.
- Gateway'e iki route eklenir (`src/Gateway/Gateway/appsettings.json`): `<s>-public`
  (`/<s>/{**remainder}`, `PathRemovePrefix: /<s>`, `RouteGroup: public`) ve, admin endpoint'i
  varsa, `<s>-admin` (`/admin/<s>/{**remainder}`, `PathPattern: /admin/{**remainder}`,
  `RouteGroup: admin`); cluster adresi base'de compose adı, `appsettings.Development.json`'da
  `localhost` portu. Portlar: gateway 5100, Auth 5103, Catalog 5104, Billing 5105, Streaming 5106; sıradaki servis 5107.
- Admin yazma command'ları `IAuditedCommand` implement eder (`AuditTargetId` explicit, JSON'a
  girmesin) ve secret taşımaz; liste endpoint'leri `Shared.Application.Paging` (`page`,
  `pageSize`, en fazla 100).
- Servisin permission'ları önce `src/Services/Auth/Auth.Domain/Roles/Permissions.cs`'te kendi
  iç içe sınıfına eklenir (`<servis>:<kaynak>:<eylem>`, küçük harf); servis aynı string'i
  `RequirePermission` ile kontrol eder. Katalogda olmayan permission bir role atanamaz.

**Başka bir servisin event'ini tüketmek** — referans: Billing'in `Customers/CreateCustomerOnUserRegistered.cs` (Auth'un `UserRegistered`'ı).

- `<S>.Application` diğer servisin sadece `*.Contracts` projesini referans alır; servisin Dockerfile'ı o projeyi de `COPY` eder.
- `IIntegrationEventHandler<TEvent>` event'i bir command'a çevirip `dispatcher.Send` ile gönderir;
  kayıt, validation ve inbox kaydı aynı pipeline/transaction'da olur. Ayrıca kayıt yok — `AddCqrs`
  handler'ı bulur, messaging de bu event'e abone olur.
- Handler'ın tam tip adı inbox anahtarıdır: işlenmiş mesajı olan bir handler'ı yeniden adlandırma
  veya taşıma, yoksa o mesajlar tekrar işlenir.
- Command handler aynı kaydı ikinci kez görürse başarılı döner (idempotent); DB'de unique index.

## Gateway — `src/Gateway/Gateway`

- İş mantığı yok; sadece `Shared.Infrastructure.API`'yi referans alır.
- Auth'tan tarayıcıya sadece `/auth/profiles/...` açık (`auth-profiles`); token dönen uçlar kapalı. Bir route kaldırdığından fazlasını eşleştirirse (`/auth/profiles` → `/auth` kaldırılır) fazlası kapsamıdır; `Location` sadece kapsam içindeyse çevrilir.
- Üç route grubu: `public`, `admin`, `webhooks` (`/<s>/webhooks/{**remainder}`, `PathRemovePrefix: /<s>`; CORS yok, kendi rate limit'i, `Cookie`/`Authorization` servise gitmez; ör. `billing-webhooks`).
- Route'un CORS ve rate limiter policy'si **grubundan** gelir (`RouteGroupConfigFilter`); route'a
  `CorsPolicy`/`RateLimiterPolicy` yazılmaz. Kurala uymayan route'la gateway başlamaz.
- Public route'tan servisin `/admin` yollarına erişim 404'tür; servisin döndüğü `Location`
  gateway yoluna çevrilir. Servisler gateway'i bilmez, kendi yollarını döner.
- İstemcinin trace header'ları devam ettirilmez; trace gateway'de başlar.
- **Tarayıcı oturumu** (`src/Gateway/Gateway/Sessions`): gateway'in kendi endpoint'leri
  `/session/...` ve `/admin/session/...`; token'lar Valkey'de şifreli, tarayıcıya sadece
  `__Host-nbp-session` / `__Host-nbp-admin` cookie'si gider. Route grubu kendi cookie'sini okur,
  servise `Authorization: Bearer` ekler, `Cookie` ve `X-CSRF-Token`'ı servise iletmez. Cookie'li
  değiştiren istek `X-CSRF-Token` ister. Refresh oturum başına tek (Valkey kilidi) ve asla tekrar
  denenmez. Ayarlar `ConnectionStrings:Valkey`, `Session`, `RateLimiting:Credentials`.

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
- **Commit mesajı değişikliği anlatır.** Claude ile yapılan commit'lerde `Co-Authored-By` satırı eklenebilir.
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
  başarısızlık `Result.Failure`, geçici sorun exception (retry → DLQ). `Data.Conflict` (yarışı kaybeden kayıt)
  geçici sayılır ve tekrar denenir. Her mesaj bir handler'a en
  fazla bir kez ulaşır (inbox). Broker'a ulaşılamazken outbox deneme saymaz, artan aralıkla
  bekler (`MessageTransportUnavailableException`); mesaj kesinti boyunca kaybolmaz.
- **Zaman:** `DateTime.UtcNow` doğrudan çağrılmaz, `TimeProvider` kullanılır.
- **ID'ler:** düz `Guid`, `Guid.CreateVersion7()`.
- **API:** Minimal API. Her uygulama `builder.AddServiceDefaults()` → `app.UseServiceDefaults()` →
  (auth) → endpoint'ler → `app.MapDefaultEndpoints()`. Endpoint'ler `result.ToHttpResult()` döner;
  hatalar RFC 9457 problem details. Yetki `RequirePermission("servis:kaynak:eylem")`.
- **Loglama:** Serilog yok; .NET `ILogger` + OpenTelemetry. `[LoggerMessage]` ile structured log,
  string interpolation yok; token/şifre/kart/e-posta loglanmaz. Hassas parametreler
  `[PersonalData]` / `[SecretData]` ile işaretlenir (projede `Microsoft.Extensions.Telemetry.Abstractions`
  doğrudan referanslı olmalı).
- **Auth endpoint'leri:** `/register`, `/sign-in`, `/sign-in/admin`, `/refresh`, `/sign-out`, `/me`,
  `/me/password` — token dönenler gateway'den proxy'lenmez, onları sadece gateway çağırır. Giriş hatası her durumda `User.InvalidCredentials` (hesabın varlığı ve kilidi
  belli olmaz); bilinmeyen e-postada da bir hash doğrulanır. Tekrar kullanılan refresh token ailenin
  tamamını iptal eder.
- **Admin (Auth):** `/admin/users`, `/admin/roles`, `/admin/permissions`. Bir rolü oluşturan,
  değiştiren, silen, atayan veya kaldıran o rolün permission'larının hepsine sahip olmalı;
  `Administrator` sistem rolüdür (tüm permission'lar, API'den değişmez, seed katalogla eşitler).
- **Çoklu dil (Catalog):** diller `Languages` sınıfında (`tr` zorunlu ve varsayılan, `en`); çeviriler kayıt başına tabloda. Admin `translations`,
  müşteri `?lang=` kullanır; çeviri yoksa Türkçe döner, yanıttaki `language` metnin gerçek dilini söyler.
- **Arama (Catalog):** Elasticsearch `ConnectionStrings:Search`, alias `catalog-titles`. Değişiklik `SearchIndexQueue`'ya aynı transaction'da yazılır, worker index'e taşır; arama `ISearchIndex` arkasında, Elasticsearch yoksa 503 `Search.Unavailable`, katalog okumaları etkilenmez. `HttpClient` değil transport ile düz JSON.
- **Billing:** Stripe gerçeğin kaynağı; `Subscription` ve `Invoice` onun aynası. Tutarlar `long` kuruş (`AmountMinor`). Kullanıcı başına tek canlı abonelik DB'de filtreli unique index. Kart için sadece marka ve son 4 hane saklanır (`CardSummary`). Stripe `IPaymentGateway` arkasında; anahtar (`Billing:Stripe:SecretKey`, lokalde `deploy/.env`'deki `STRIPE_SECRET_KEY`) yoksa ödeme uçları 503. Ödeme `POST`'ları `Idempotency-Key` ister (`IdempotencyFilter`; sadece 2xx saklanır, farklı gövde 422, eş zamanlı 409). Event'ler `Billing.Contracts`: `SubscriptionActivated`/`Suspended`/`Canceled` (artan `Revision` taşır; tüketici eskisini yok sayar) ve `PaymentFailed`. Stripe webhook'u `POST /webhooks/stripe`: imza (`Billing:Stripe:WebhookSecret`, lokalde `STRIPE_WEBHOOK_SECRET`), inbox, nesne Stripe'tan güncel haliyle okunup aynalanır; geçici hatada inbox'a yazılmaz, Stripe tekrar dener. 422 `Error.Unprocessable`, 502 `Error.UpstreamFailure`.
- **Profiller (Auth):** `User` aggregate'inde, hesap başına en fazla 5, ad + `MaxAgeRating` (Catalog'un yaş değerleri); kayıtta "Main". Event'ler `ProfileCreated`/`Changed`/`Removed` (`Revision`).
- **Videolar (Streaming):** admin SAS ile `video-uploads`'a yükler, `/admin/videos/{id}/uploaded` ile onaylar → `TranscodeJobs` (Postgres, `SKIP LOCKED` + lease). `Streaming.Transcoder` HLS üretir (360/720/1080, kaynağı aşmadan) → private `videos/{id:N}/`. Event'ler `Streaming.Contracts`: `VideoQueued`/`Ready`/`Failed` (`Revision`). Storage CORS hesap geneli: kurallar eklenir, ezilmez (`StorageCors.MergeAsync`); origin'ler `Videos:UploadOrigins`/`PlaybackOrigins`, blob bağlantısı `ConnectionStrings:Videos`.
- **Video durumu (Catalog):** `ContentVideo` event'lerle güncellenir, `Version`'ı artırmaz; film/bölüm oynatılabilir video olmadan yayınlanmaz (409 `Content.VideoRequired`).
- **Yerel kopyalar (Streaming):** oynatma kararı için abonelik (`Subscriptions`, abonelik başına), profil (`Profiles`, silinen işaretli kalır) ve izlenebilir içerik (`PlayableTitles`/`PlayableItems`) kopyaları; sadece event'le değişir, revision büyükse uygulanır (`Revision` concurrency token). Catalog'un `PlayableChanged`'i (`Catalog.Contracts`) başlığın tam listesini taşır; `ContentItem.PlayabilityRevision` `Version`'dan ayrı.
- **Playback (Streaming):** `POST /playback-sessions` kuralları kopyalardan kontrol eder (profil → abonelik → içerik → yaş), ekran sınırı `Viewer` aggregate'inde (`Version` token); heartbeat (30 sn, 2 dk sessizlik oturumu bitirir) kuralları yeniden kontrol eder, heartbeat ve bitirme `ExecuteUpdate`. Playlist'ler `/playback/{id}/...m3u8` token'sız, HMAC imzalı (`Playback:SigningKey`, `dev.ps1 secrets` bir kez üretir); segmentler `videos`'tan okuma SAS'ı. Plan kalitesi yüksekliği sınırlar (Sd ≤480, Hd ≤1080).
- **İzleme konumu (Streaming):** heartbeat/bitirme `positionSeconds` taşır → `WatchProgress` (profil + içerik, upsert); oturum yanıtında `resumeFromSeconds`; `GET /profiles/{id}/progress?titleId=` ve `/continue-watching` (en fazla 20, sonraki bölüm `PlayableItem.Sequence` ile). Başlık kopyası eşit revision'ı yeniden uygular (contract'a eklenen alan backfill'le gelsin).
- **Eş zamanlı yazma:** kaydetmede yarışı kaybeden command (concurrency, unique, FK) 409 `Data.Conflict` alır. Düzenlenen
  aggregate'ler `Version` taşır; istemci yüklediği değeri geri gönderir (ör. Catalog `ContentItem`).
- **Audit:** `IAuditedCommand` işaretli başarılı command'lar aynı transaction'da
  `AuditLogEntries`'e yazılır (`AuditBehavior`, tablo `ServiceDbContext`'te).
- **JWT:** ES256, `typ: at+jwt`, 15 dk; servisler Auth'un JWKS'ini `Authority` üzerinden bulur,
  başka algoritma veya tip kabul edilmez. Claim adları olduğu gibi (`sub`, `role`, `permission`).
  İmza anahtarları `Auth:Tokens` altında, asla loglanmaz; rotasyon adımları ARCHITECTURE §12.3.1.
- **Lisans kontrolü:** proje canlıya alınmayacak (portföy/bitirme projesi); açık kaynak veya ticari olmayan kullanımda
  ücretsiz paketler serbest, bu kullanım için ödeme isteyenler değil. Yeni bir paket eklemeden önce lisansını kontrol et.
