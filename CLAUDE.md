# CLAUDE.md — NetBoxPlus-Backend (v2)

NetBoxPlus — Netflix benzeri video/abonelik platformu. Bu repo tüm backend'in monorepo'su: .NET 10
mikroservisler, servis başına Clean Architecture, ortak kod `src/BuildingBlocks/` altında.
Her geliştirmeden sonra bu dosya ve ilgili doküman güncellenir.

- Kararlar ve gerekçeleri: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
- Aktif faz ve iş dökümü: [docs/PHASE-7.md](docs/PHASE-7.md) (bitenler: [docs/PHASE-0.md](docs/PHASE-0.md), [docs/PHASE-1.md](docs/PHASE-1.md), [docs/PHASE-2.md](docs/PHASE-2.md), [docs/PHASE-3.md](docs/PHASE-3.md), [docs/PHASE-4.md](docs/PHASE-4.md), [docs/PHASE-5.md](docs/PHASE-5.md), [docs/PHASE-6.md](docs/PHASE-6.md))

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
src/Tools/            NetBoxPlus.DemoSeeder (demo verisi; servis koduna referans yok)
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
ile yapar (`tests/TestSupport/DockerAvailability.cs`, csproj'a link'lenir). Postgres'e EF Core ile bağlanan
her fixture bağlantı dizesini `ContainerConnections.ConnectionString(...)` ile kurar ve
`RetryLostConnectionAttempts<TContext>()` ekler (`tests/TestSupport/ContainerConnections.cs`): Docker Desktop'ın
port yönlendirmesi yük altında tek tük bağlantıyı kabul edip container'a hiç iletmiyor; açılış 5 s'de kesilip
yeniden denenir (timeout'u büyütmek işe yaramaz, kayıp bağlantı hiç toparlanmaz). Aynı kural RabbitMQ ve Azurite'te:
testin kendi broker bağlantısı `BrokerConnections.OpenAsync` (deneme 5 sn'de bırakılır; istemcinin el sıkışma beklemesi token'ı
dinlemiyor), Azurite'e giden her client `StorageConnections.CreateClient`/`CreateHttpClient` (istek başına 10 sn, 3 deneme); fixture'lar
servisin `BlobServiceClient`'ını bununla değiştirir. Dayanıklılık `BlackHoleProxy` (ilk bağlantıyı kabul edip yutan TCP proxy) ile test edilir. Test image'ları
`deploy/compose.yaml` ile aynı sürümde tutulur.

**CI** (`.github/workflows/ci.yml`, `v2` ve `master`'a push ve PR'larda): Release build (uyarı =
hata) → `docs/openapi` build çıktısıyla aynı değilse kırılır → tüm testler (Docker zorunlu) →
paralel olarak her Dockerfile'ın image build'i (push yok). API değiştiyse build'i lokalde çalıştırıp
`docs/openapi/*.json`'ı da commit'le. Transcode ve demo seeder testleri gerçek **ffmpeg** ister (seeder Linux'ta `fonts-dejavu-core` da): `FFMPEG_DIRECTORY`
ya da PATH (`tests/TestSupport/FfmpegAvailability.cs`); yoksa lokalde atlanır, CI'da (ffmpeg `apt` ile
kurulur) kırar. Lokalde `winget install Gyan.FFmpeg`. Action'lar commit SHA'sıyla sabit; Dependabot (actions + NuGet)
haftalık PR'ları `v2`'ye açar.

Lokal bağımlılıklar (Postgres, RabbitMQ, Valkey, Azurite, Mailpit, Aspire dashboard; `-Search` ile Elasticsearch):

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
- `NETBOXPLUS_DATABASES`'e eklenen database ve rolü bir sonraki `up`'ta mevcut volume'da oluşur
  (`deploy/postgres/init/01-service-databases.sh` idempotent, `up` onu tekrar çalıştırır); veri silinmez.
- E-postalar lokalde Mailpit'e gider (SMTP 1025, arayüz ve API http://localhost:8025). Brevo (gerçek
  gönderim) sadece `.env`'de `EMAIL_PROVIDER=Brevo` ise kullanılır, `BREVO_API_KEY` ve `EMAIL_FROM`
  ister; anahtarın varlığı tek başına geçirmez. `secrets` ve `up -App` aynı kuralı uygular.

Bütün sistemi container'larda çalıştırmak (bağımlılıklar + Elasticsearch + beş Migrator, beş API,
Transcoder ve gateway; compose `app` profili, image'lar `netboxplus/<ad>:local` olarak build edilir):

```powershell
./deploy/dev.ps1 up -App
```

- Image'lar tek tek build edilir (hepsi birden Docker Desktop'ı kilitleyebiliyor). Migrator'lar bir kez
  çalışıp çıkar, API'ler onlar başarıyla bitince başlar; `up` hepsinin `/health/ready`'si cevap verene kadar
  bekler. Tarayıcı sadece gateway'i (`localhost:5100`) bilir.
- `-Observability` (sadece `-App` ile): compose `observability` profili, `grafana/otel-lgtm` (Grafana +
  Prometheus + Loki + Tempo); app container'larının OTLP'si Aspire yerine oraya gider
  (`NETBOXPLUS_OTLP_ENDPOINT`). Grafana http://localhost:3000, `NetBoxPlus` dashboard'u
  `deploy/observability/dashboards/`'tan provision edilir. IDE'den çalışan servisler Aspire'a göndermeye devam eder.
- Container'lar birbirine compose adıyla bağlanır; Azurite'in tarayıcıya verilen SAS/görsel adresleri
  `BlobStorage:PublicEndpoint` ile `127.0.0.1:10000`'e çevrilir (imza host'u kapsamaz).
- İmza anahtarları (`AUTH_TOKEN_KEY_ID`, `AUTH_TOKEN_SIGNING_KEY`, `PLAYBACK_SIGNING_KEY`) `.env`'de;
  ilk çalıştırmada üretilir (varsa User Secrets'takiler taşınır) ve hem `secrets` hem compose oradan okur.
- `down`, `ps`, `logs`, `reset` app container'larını da kapsar.

Yük testi (k6, compose `tools` profili; senaryolar ve sonuçlar `deploy/load/`):

```powershell
./deploy/dev.ps1 load -Scenario prepare
./deploy/dev.ps1 load
```

- `prepare` bir kez: `load-user-NN@load.netboxplus.test` hesapları (şifre `.env`'de `LOAD_USER_PASSWORD`) ve Stripe test
  modunda Premium abonelik; Stripe CLI'ı kendisi başlatır, `STRIPE_WEBHOOK_SECRET` yoksa alıp `.env`'ye yazar ve Billing'i
  yeniden başlatır. Aboneliği olan hesaba dokunmaz.
- `load` (`-Scenario mixed|browse|watch`): gateway'i koşu boyunca `deploy/load/compose.load.yaml` ile yüksek rate
  limit'le çalıştırır (k6'nın bütün sanal kullanıcıları tek IP), bitince eski haline döner. Özet `deploy/load/results/`
  (git'te yok); Grafana açıksa k6 metrikleri de oraya gider. Kayıtlar `load-r-<koşu>-*` etiketlidir.

Demo verisi (`up -App` çalışırken; compose `tools` profili, `demo-seed` image'ı):

```powershell
./deploy/dev.ps1 seed
```

- `src/Tools/NetBoxPlus.DemoSeeder` admin panelinin yaptığını yapar: gateway'den admin girişi, uydurma 6 tür,
  10 kişi, 6 film, 2 dizi (2 sezon × 3 bölüm), ffmpeg ile sentetik görsel ve 30 sn'lik video, transcode bekleme,
  bir filme dublaj ve iki dilde altyazı, yayınlama. Veritabanına ve servis koduna dokunmaz. Yaklaşık 4 dakika.
- İdempotent: her adım önce mevcut durumu kontrol eder (eşleştirme Türkçe başlık + `originalTitle`, kişi adı,
  türün Türkçe adı); ikinci çalıştırma değişiklik yapmaz, yarıda kalan devam eder. Gateway'in 429'una
  `Retry-After` ile uyar.
- `STRIPE_SECRET_KEY` varsa (sadece `sk_test_`/`rk_test_`) Stripe'ta aylık fiyat açar (`lookup_key`
  `netboxplus-demo-*`) ve Temel (Sd, 1 ekran) / Premium (Hd, 4 ekran) planlarını ekleyip aktifler; yoksa atlar.
- Host'tan da çalışır: `dotnet run --project src/Tools/NetBoxPlus.DemoSeeder` ve `Seeder__AdminEmail`,
  `Seeder__AdminPassword`, gerekirse `Seeder__FfmpegPath`, `Seeder__StripeSecretKey`.

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
`stripe listen --api-key <anahtar> --events checkout.session.completed,customer.subscription.created,customer.subscription.updated,customer.subscription.deleted,invoice.finalized,invoice.paid,invoice.payment_failed,invoice.voided,invoice.marked_uncollectible --forward-to localhost:5100/billing/webhooks/stripe`
(CLI artık `--events` olmadan iletmiyor; liste Billing'in işlediği event'ler).

Streaming videoları transcode eden ayrı süreç (aynı database ve User Secrets; ffmpeg PATH'te veya
`Transcoder:FfmpegPath`/`FfprobePath`): `dotnet run --project src/Services/Streaming/Streaming.Transcoder`.
RabbitMQ'ya bağlanmaz; event'lerini Streaming.API'nin outbox işlemcisi gönderir, o da çalışmalı. Log, trace ve
metrikleri servislerle aynı ayarla `builder.AddWorkerDefaults()` (Shared.Infrastructure.API, HTTP'siz) gönderir;
bu yüzden image'ı `aspnet:10.0` tabanlı.

Başka servisin verisini kopyalayan bir tüketici sonradan açıldıysa, kaynak servisin Migrator'ı mevcut
durumu yeniden yayınlar (outbox'ı servisin API'si gönderir): `-- announce-profiles` ve `-- announce-email-verifications` (Auth),
`-- announce-subscriptions` (Billing), `-- announce-playable` (Catalog). Örnek:
`dotnet run --project src/Services/Auth/Auth.Migrator -- announce-profiles`.

Diğer servisler de aynı şekilde (`<S>.Migrator`, `<S>.API`). Gateway (`http://localhost:5100`;
Development'ta Auth'a `localhost:5103`, Catalog'a `localhost:5104`, Billing'e `localhost:5105`, Streaming'e `localhost:5106`, Notification'a `localhost:5107` üzerinden gider; tarayıcı oturumları için Valkey ve Auth çalışmalı):

```powershell
dotnet run --project src/Gateway/Gateway --launch-profile Gateway
```

`secrets`, `deploy/.env`'deki değerleri (imza anahtarları dahil) servisin User Secrets'ına
(`netboxplus-<servis>`) yazar. Auth'un
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
- API ve Migrator için Dockerfile Auth'takiler gibi (chiseled, non-root; Migrator `runtime` image'ı,
  port yok). Yeni servis `deploy/dev.ps1`'deki `$services` listesine ve `Wait-AppReady`'ye,
  `deploy/compose.yaml`'daki `NETBOXPLUS_DATABASES`'e ve `app` profiline (`<s>-migrator` + `<s>-api`,
  `x-app-values` anchor'larıyla), `tests/Architecture.Tests` csproj'una ve `.github/workflows/ci.yml`'deki
  `image` matrix'ine (API ve Migrator) eklenir.
- Gateway'e iki route eklenir (`src/Gateway/Gateway/appsettings.json`): `<s>-public`
  (`/<s>/{**remainder}`, `PathRemovePrefix: /<s>`, `RouteGroup: public`) ve, admin endpoint'i
  varsa, `<s>-admin` (`/admin/<s>/{**remainder}`, `PathPattern: /admin/{**remainder}`,
  `RouteGroup: admin`); cluster adresi base'de compose adı, `appsettings.Development.json`'da
  `localhost` portu. Portlar: gateway 5100, Auth 5103, Catalog 5104, Billing 5105, Streaming 5106, Notification 5107; sıradaki servis 5108.
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
- Auth'tan tarayıcıya sadece `/auth/profiles/...` (`auth-profiles`), `/auth/email-verification/...` (`auth-email-verification`) ve `/auth/email-change/...` (`auth-email-change`) açık; token dönen uçlar kapalı. Bir route kaldırdığından fazlasını eşleştirirse (`/auth/profiles` → `/auth` kaldırılır) fazlası kapsamıdır; `Location` sadece kapsam içindeyse çevrilir.
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
  denenmez. Şifre sıfırlama da burada (`/session/password-reset`, `/confirm`): oturum ve cookie
  istemez, Auth'un yanıtını aynen geçirir. E-posta değiştirme isteği `/session/email` (oturum + CSRF, Auth'un `/me/email`'ine,
  yanıt aynen; oturum değişmez). Hesap silme `/session/deletion` (oturum + CSRF, Auth'un `/me/deletion`'ına; başarıda
  oturum Valkey'den silinir ve cookie temizlenir, Auth'a ayrıca sign-out gitmez). Parola alan veya e-posta gönderen uçlar `Credentials`
  limitindedir. Ayarlar `ConnectionStrings:Valkey`, `Session`, `RateLimiting:Credentials`.

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
  `MessageRetentionService` gönderilmiş outbox satırlarını 7 gün, inbox satırlarını 30 gün sonra siler
  (saatte bir, 1000'lik parçalar; gönderilmemiş/bırakılmış outbox satırı silinmez; ayarlar `MessageRetention`;
  Transcoder'da kapalı). `OutboxMonitor` outbox'ı 30 sn'de bir sayar (`Outbox:MonitorInterval`): bekleyen,
  bırakılmış (10 deneme) ve en eski bekleyenin yaşı metrikte; bırakılmış mesaj sayısı değiştikçe bir Error log
  ve `outbox` health check'i `Degraded` (200, trafik kesilmez). Paylaşılan tablolara (outbox, inbox, audit) dokunan değişiklik beş servise de migration
  ister; `Architecture.Tests/MigrationTests` unutulanı yakalar.
- **Messaging:** `services.AddRabbitMqMessaging()`, ayarlar `Messaging` bölümünde. Integration
  event handler'ları `IIntegrationEventHandler<T>` implement eder ve `Task<Result>` döner: kalıcı
  başarısızlık `Result.Failure`, geçici sorun exception (retry → DLQ). `Data.Conflict` (yarışı kaybeden kayıt)
  geçici sayılır ve tekrar denenir. Her mesaj bir handler'a en
  fazla bir kez ulaşır (inbox). Broker'a ulaşılamazken outbox deneme saymaz, artan aralıkla
  bekler (`MessageTransportUnavailableException`); mesaj kesinti boyunca kaybolmaz. `DeadLetterMonitor`
  servisin DLQ'sunu dakikada bir pasif declare ile sayar (`Messaging:DeadLetterCheckInterval`; kuyruk henüz
  yoksa 5 sn sonra tekrar): sayı değiştikçe bir Error log, `dead-letters` health check'i `Degraded`.
  Hiçbir event tüketmeyen serviste çalışmaz.
- **Zaman:** `DateTime.UtcNow` doğrudan çağrılmaz, `TimeProvider` kullanılır.
- **ID'ler:** düz `Guid`, `Guid.CreateVersion7()`.
- **API:** Minimal API. Her uygulama `builder.AddServiceDefaults()` → `app.UseServiceDefaults()` →
  (auth) → endpoint'ler → `app.MapDefaultEndpoints()`. Endpoint'ler `result.ToHttpResult()` döner;
  hatalar RFC 9457 problem details. Yetki `RequirePermission("servis:kaynak:eylem")`.
- **Loglama:** Serilog yok; .NET `ILogger` + OpenTelemetry. `[LoggerMessage]` ile structured log,
  string interpolation yok; token/şifre/kart/e-posta loglanmaz. Hassas parametreler
  `[PersonalData]` / `[SecretData]` ile işaretlenir (projede `Microsoft.Extensions.Telemetry.Abstractions`
  doğrudan referanslı olmalı).
- **Metrikler:** `System.Diagnostics.Metrics`, meter adı `NetBoxPlus.<Ad>` (`AddServiceDefaults`/`AddWorkerDefaults`
  `NetBoxPlus.*`'ı dışa aktarır, ayrıca kayıt yok), instrument adı `netboxplus.*`, `IMeterFactory` ile singleton
  bir metrik sınıfında. Etiketlerde id, e-posta, ad yok; sadece sınırlı değerler (sonuç, tür, hata kodu).
  Veritabanından okunan gauge'lar arka planda (30 sn) ölçülür, callback sadece son değeri okur.
  Mevcutlar: outbox, messaging (işlenen/tekrar/DLQ, süre, DLQ derinliği), Auth giriş, Streaming transcode ve
  oynatma, Notification e-posta. Health check servisinin logu Error seviyesinde: `Degraded` her probe'da tekrar
  loglanmaz (olayı izleyicisi bir kez loglar), `Unhealthy` loglanır. Container'lar metriği 15 sn'de bir gönderir.
- **Auth endpoint'leri:** `/register`, `/sign-in`, `/sign-in/admin`, `/refresh`, `/sign-out`, `/me`,
  `/me/password`, `/me/email`, `/me/deletion`, `/email-verification` (GET durum, POST yeniden gönder), `/email-verification/confirm`,
  `/email-change/confirm`, `/password-reset`, `/password-reset/confirm` — token dönenler gateway'den proxy'lenmez, onları sadece gateway çağırır. `/register` hesabı ve
  ilk müşteri oturumunu tek transaction'da açar (201, id + token çifti); gateway'in kaydı ayrıca `/sign-in` yapmaz (ikinci PBKDF2 yok). Giriş hatası her durumda `User.InvalidCredentials` (hesabın varlığı ve kilidi
  belli olmaz); bilinmeyen e-postada da bir hash doğrulanır. Tekrar kullanılan refresh token ailenin
  tamamını iptal eder.
- **Admin (Auth):** `/admin/users` (`DELETE /admin/users/{id}` ayrı permission `auth:users:delete`), `/admin/roles`, `/admin/permissions`. Bir rolü oluşturan,
  değiştiren, silen, atayan veya kaldıran o rolün permission'larının hepsine sahip olmalı;
  `Administrator` sistem rolüdür (tüm permission'lar, API'den değişmez, seed katalogla eşitler).
- **Çoklu dil (Catalog):** diller `Languages` sınıfında (`tr` zorunlu ve varsayılan, `en`); çeviriler kayıt başına tabloda. Admin `translations`,
  müşteri `?lang=` kullanır; çeviri yoksa Türkçe döner, yanıttaki `language` metnin gerçek dilini söyler.
- **Arama (Catalog):** Elasticsearch `ConnectionStrings:Search`, alias `catalog-titles`. Değişiklik `SearchIndexQueue`'ya aynı transaction'da yazılır, worker index'e taşır; arama `ISearchIndex` arkasında, Elasticsearch yoksa 503 `Search.Unavailable`, katalog okumaları etkilenmez. `HttpClient` değil transport ile düz JSON.
- **Billing:** Stripe gerçeğin kaynağı; `Subscription` ve `Invoice` onun aynası. Tutarlar `long` kuruş (`AmountMinor`). Kullanıcı başına tek canlı abonelik DB'de filtreli unique index. Kart için sadece marka ve son 4 hane saklanır (`CardSummary`). Stripe `IPaymentGateway` arkasında; anahtar (`Billing:Stripe:SecretKey`, lokalde `deploy/.env`'deki `STRIPE_SECRET_KEY`) yoksa ödeme uçları 503. Ödeme `POST`'ları `Idempotency-Key` ister (`IdempotencyFilter`; sadece 2xx saklanır, farklı gövde 422, eş zamanlı 409). Event'ler `Billing.Contracts`: `SubscriptionActivated`/`Suspended`/`Canceled` (artan `Revision` taşır; tüketici eskisini yok sayar; `Activated.Reason` `Started`/`Resumed`/`PlanChanged`; `announce-subscriptions` yeniden yayınladığında `Replayed: true` ve `Reason` boş) ve `PaymentFailed`. Contract'a sonradan eklenen alan opsiyonel (varsayılan değerli) olur. Stripe webhook'u `POST /webhooks/stripe`: imza (`Billing:Stripe:WebhookSecret`, lokalde `STRIPE_WEBHOOK_SECRET`), inbox, nesne Stripe'tan güncel haliyle okunup aynalanır; geçici hatada inbox'a yazılmaz, Stripe tekrar dener. 422 `Error.Unprocessable`, 502 `Error.UpstreamFailure`.
- **Profiller (Auth):** `User` aggregate'inde, hesap başına en fazla 5, ad + `MaxAgeRating` (Catalog'un yaş değerleri); kayıtta "Main". Event'ler `ProfileCreated`/`Changed`/`Removed` (`Revision`).
- **E-posta doğrulama (Auth):** `UserTokens` (sadece SHA-256 hash, tek kullanımlık `ExecuteUpdate`, 24 saat); kayıtta ve `POST /email-verification` (oturum açık, 60 sn'de bir, öncekiler iptal) ile `EmailVerificationRequested` (ham token, token id) yayınlanır, Notification e-postalar. `POST /email-verification/confirm` anonim; her hata aynı 400 `EmailVerification.TokenInvalid`. Durum `User.EmailVerifiedAt`, `/me`'de `emailVerified`, tarayıcı için `GET /email-verification`. Giriş için zorunlu değil;
  **yeni abonelik (checkout) için zorunlu**. İlk doğrulamada (link veya şifre sıfırlama) bir kez `EmailVerified` (adresle birlikte)
  yayınlanır; Billing `BillingCustomer.EmailVerifiedAt`'e yazar (müşteri yoksa adresten açar, en erken zaman kalır) ve
  doğrulanmamış müşterinin `POST /checkout-sessions`'ı 403 `Checkout.EmailNotVerified`. Portal ve mevcut abonelik etkilenmez.
- **Şifre sıfırlama (Auth):** aynı `UserTokens` (`PasswordReset`, 1 saat). `POST /password-reset` anonim, hesap olsun olmasın (kapalı, 60 sn içinde tekrar) hep 202; sadece bozuk adres 400. Onay `POST /password-reset/confirm`: önce şifre kuralı (alan hatası), sonra her token hatası aynı 400 `PasswordReset.TokenInvalid`; başarıda şifre değişir, refresh token'ların hepsi hemen iptal, kilit kalkar, e-posta doğrulanmış sayılır, oturum açılmaz. Şifre her değiştiğinde (`ChangePassword` veya sıfırlama; giriş sırasındaki rehash değil) `PasswordChanged` (`ChangeId`) yayınlanır, Notification bildirim e-postası gönderir.
- **Videolar (Streaming):** admin SAS ile `video-uploads`'a yükler, `/admin/videos/{id}/uploaded` ile onaylar → `TranscodeJobs` (Postgres, `SKIP LOCKED` + lease). `Streaming.Transcoder` HLS üretir (360/720/1080, kaynağı aşmadan) → private `videos/{id:N}/`. Event'ler `Streaming.Contracts`: `VideoQueued`/`Ready`/`Failed` (`Revision`). Storage CORS hesap geneli: kurallar eklenir, ezilmez (`StorageCors.MergeAsync`); origin'ler `Videos:UploadOrigins`/`PlaybackOrigins`, blob bağlantısı `ConnectionStrings:Videos`.
- **Ses izleri / dublaj (Streaming):** dil kodları bölgeli BCP 47, sabit liste `MediaLanguages` (`tr-TR`, `en-US`, `en-GB`, `de-DE`, `fr-FR`, `es-ES`). Yeni videoda ses kalitelerden ayrı (`AudioLayout.Separate`): kaliteler sessiz, kaynağın sesi `audio/original/` (dili `originalAudioLanguage`, varsayılan `tr-TR`). Dublaj `POST /admin/videos/{id}/audio-tracks` (`language`) → SAS → `.../audio-tracks/{trackId}/uploaded` → transcode kuyruğu (`TranscodeJobs.Id` = video ya da iz id'si) → `audio/{trackId:N}/`; süresi videodan 2 sn'den fazla farklıysa `Failed`. Dil başına tek iz; `DELETE .../audio-tracks/{language}` (orijinal 409 `Video.OriginalAudioKept`). Master playlist `#EXT-X-MEDIA:TYPE=AUDIO` (orijinal varsayılan), kaliteler `AUDIO="audio"`; ses playlist'i `/playback/{id}/audio/{original|N}/index.m3u8`. Eski videolar (`Muxed`) aynen oynar, dublaj alamaz (409 `Video.LayoutOutdated`).
- **Altyazı (Streaming):** `POST /admin/videos/{id}/subtitles` (`language`, video hazır olmalı, eski düzen de olur) → SAS (en fazla 1 MB, WebVTT veya SRT) → `.../subtitles/{subtitleId}/uploaded`: API hemen okur, SRT'yi VTT'ye çevirir (`SubtitleFormat`), `subtitles/{id:N}.vtt` (`text/vtt`) yazar; geçersiz 400 `Video.SubtitleInvalid` (aynı adrese tekrar yüklenebilir). Dil başına tek; `DELETE .../subtitles/{language}`. Master'da `#EXT-X-MEDIA:TYPE=SUBTITLES` (`DEFAULT=NO`), kaliteler `SUBTITLES="subtitles"`; altyazı playlist'i oturum başına üretilir (`/playback/{id}/subtitles/{N}.m3u8`, tek segment = VTT'nin SAS'ı).
- **Diller (Streaming → Catalog, tercih):** hazır videonun sunduğu diller değişince `Video.Revision` artar, `VideoTracksChanged` (ses dilleri orijinal başta, altyazı dilleri) yayınlanır; Catalog `ContentVideo`'da video başına tutar (daha yeni video ya da büyük revision; sıra fark etmez), müşteriye oynatılabilir videonunkini gösterir: `GET /movies/{id}`, bölümler ve dizi (birleşim) `audioLanguages`/`subtitleLanguages`. Profil başına tercih `GET`/`PUT /profiles/{id}/playback-preferences` (`audioLanguage`, `subtitleLanguage`; `null` = orijinal / kapalı; profil ve hesap silinince silinir): master playlist'te video o dili sunuyorsa `DEFAULT=YES`. `POST /playback-sessions` yanıtı sunulan dilleri taşır. Demo seeder "Kuzey Işığı"na `en-US` dublaj ve `tr-TR`/`en-US` altyazı ekler (video eski düzendeyse önce yeniden yükler).
- **Video durumu (Catalog):** `ContentVideo` event'lerle güncellenir, `Version`'ı artırmaz; film/bölüm oynatılabilir video olmadan yayınlanmaz (409 `Content.VideoRequired`).
- **Yerel kopyalar (Streaming):** oynatma kararı için abonelik (`Subscriptions`, abonelik başına), profil (`Profiles`, silinen işaretli kalır) ve izlenebilir içerik (`PlayableTitles`/`PlayableItems`) kopyaları; sadece event'le değişir, revision büyükse uygulanır (`Revision` concurrency token). Catalog'un `PlayableChanged`'i (`Catalog.Contracts`) başlığın tam listesini taşır; `ContentItem.PlayabilityRevision` `Version`'dan ayrı.
- **Playback (Streaming):** `POST /playback-sessions` kuralları kopyalardan kontrol eder (profil → abonelik → içerik → yaş), ekran sınırı `Viewer` aggregate'inde (`Version` token); heartbeat (30 sn, 2 dk sessizlik oturumu bitirir) kuralları yeniden kontrol eder, heartbeat ve bitirme `ExecuteUpdate`. Playlist'ler `/playback/{id}/...m3u8` token'sız, HMAC imzalı (`Playback:SigningKey`, `dev.ps1 secrets` bir kez üretir); segmentler `videos`'tan okuma SAS'ı. Plan kalitesi yüksekliği sınırlar (Sd ≤480, Hd ≤1080).
- **İzleme konumu (Streaming):** heartbeat/bitirme `positionSeconds` taşır → `WatchProgress` (profil + içerik, upsert); oturum yanıtında `resumeFromSeconds`; `GET /profiles/{id}/progress?titleId=` ve `/continue-watching` (en fazla 20, sonraki bölüm `PlayableItem.Sequence` ile). Başlık kopyası eşit revision'ı yeniden uygular (contract'a eklenen alan backfill'le gelsin).
- **Listem (Streaming):** profil başına `MyList` aggregate'i (id = profil id, `Version` concurrency token; en fazla 200, aynı başlık bir kez), tablolar `MyLists`/`MyListEntries`. `GET /profiles/{id}/my-list` (son eklenen başta; sadece şu an izlenebilir — başlık kopyasında öğe var — ve profilin yaşına uygun başlıklar, diğerleri silinmez gizlenir), `PUT`/`DELETE /profiles/{id}/my-list/{titleId}` (204; izlenemez 404 `Playback.NotAvailable`, yaş 403 `Playback.AgeRestricted`, dolu 409 `MyList.Full`; abonelik gerekmez). Profil kullanıcının değil ya da kaldırılmışsa 404 `Playback.ProfileNotFound`. `ProfileRemoved` profilin, `UserDeleted` kullanıcının listelerini siler. Event yayınlanmaz.
- **Popülerlik (Streaming → Catalog):** Streaming son 7 günü (bugün dahil, UTC) başlık başına sayar: profil başına günde bir. Biten günler bir kez `DailyPlays`'e sayı olarak yazılır (kim izledi tutulmaz), bugün oturumlardan canlı; bu yüzden `PlaybackSessions` 2 gün saklanır. `PopularityService` (API, başlangıçta ve `Popularity:Interval`, varsayılan 1 saat; son hesap yeniyse atlar) tek satırlık `PopularityRanking`'i (`Version` token) günceller, ilk 100 değiştiyse `PopularityUpdated` (`Revision`, tam liste) yayınlar. Catalog `PopularityCopy`/`TitlePopularity`'de tutar, skoru değişen yayındaki başlıkları index kuyruğuna ekler; `GET /titles?sort=Popular` (skor, sonra yayın tarihi; varsayılan `Newest`), aramada `popularity` alanı `function_score` (`log1p`, `boost_mode: sum`) ile hafif öne çeker. Alan mevcut index'e `EnsureReadyAsync`'te `PUT _mapping` ile eklenir.
- **E-posta değiştirme (Auth):** `POST /me/email` (şifre + yeni adres; önce şifre, yanlışsa başarısız giriş sayılır; sonra 400 `User.EmailUnchanged`, 409 `User.EmailTaken`, 60 sn'de bir 409 `EmailChange.RequestedRecently`, öncekiler iptal) → `UserTokens` `EmailChange` (24 saat, yeni adres token'ın `Email` kolonunda) → `EmailChangeRequested` (yeni adresle). `POST /email-change/confirm` anonim; token hataları ve kapalı hesap aynı 400 `EmailChange.TokenInvalid`, adres bu arada alındıysa 409 `User.EmailTaken`. Başarıda adres değişir ve doğrulanmış sayılır (ilk doğrulamaysa `EmailVerified` de), kullanıcının diğer kullanılmamış token'ları (eski adrese gitmişlerdi) iptal, oturumlar kalır. `User.EmailRevision` (kayıtta 1) artar, `EmailChanged` (`Revision`) yayınlanır; kopyalar (Billing müşterisi + Stripe müşterisi, Notification alıcısı) daha büyük revision'ı uygular, kopya yoksa oluşturur.
- **Hesap silme:** müşteri `POST /me/deletion` (şifre; yanlışsa başarısız giriş) veya admin `DELETE /admin/users/{id}` (kendisi 409 `User.CannotDeleteSelf`, son Administrator 409 `User.LastAdministrator`, rollerinin permission'larının hepsini tutmayan 403). Geri alma yok. Auth satırı silmez, anonimleştirir (`deleted-{id:N}@deleted.invalid`, şifre hash'i `deleted`, rol/profil yok, `DeletedAt`; `Users`'ta query filter, adres yeniden kullanılabilir); refresh token'lar hemen, e-posta linkleri kayıtla iptal. `UserDeleted` (adres taşımaz) → Billing Stripe müşterisini siler (abonelik Stripe'ta hemen, iadesiz iptal; ayna webhook'la), müşteriyi anonimleştirir (`DeletedAt`, sonraki değişiklik/doğrulama uygulanmaz), faturalar kalır ama kart özeti silinir; Streaming profil kopyalarını kalıcı kaldırır (`RemoveForGood`, geç gelen profil event'i diriltemez), izleme konumlarını siler, açık oturumları bitirir; Notification veda e-postasını alıcının adresine sabitleyip kuyruğa ekler, bekleyen e-postaları siler, diğerlerinin içeriğini boşaltır, alıcıyı adressiz bırakır (`DeletedAt`); silinmiş alıcıya sonradan kuyruğa giren e-posta gönderilmez (`Failed`, içerik silinir). Silme kayıttan önce gelirse kopyalar silinmiş olarak açılır, geç gelen kayıt bir şey getirmez.
- **Notification:** tamamen event'le (port 5107, `notification` DB); alıcı kopyası `Recipients` (`UserRegistered`, `EmailChanged`, `UserDeleted`). E-posta `IEmailSender` arkasında: SMTP (MailKit; lokalde Mailpit) veya Brevo (düz HTTP, `Email:Brevo:ApiKey`); ayarlar `Email` bölümünde, geçersizse servis başlamaz. Sonuç gönderildi / tekrar dene (ulaşılamadı, 4xx SMTP, 429, 5xx) / reddedildi (5xx SMTP, diğer 4xx); adres ve anahtar loglanmaz. E-postalar `OutgoingEmails` kuyruğunda (event başına tek, `Kind` + `SourceKey` unique); `EmailSendingService` `SKIP LOCKED` + lease ile gönderir, artan bekleme, 8 denemede `Failed`; gönderilenin gövdesi hemen, başarısızınki 7 gün sonra silinir. Adres gönderimde alıcı kopyasından okunur; alıcının güncel adresine gitmeyecek e-posta (değiştirme linki yeni adrese, "adresin değişti" bildirimi eski adrese) adresi `ToAddress`'e sabitler, gövdeyle birlikte silinir. Admin `/admin/emails` (`notification:emails:read`/`write`), gateway `/admin/notification/...`. Billing e-postaları (`QueueBillingEmails`): abonelik başladı / ödeme alındı (`Activated` `Started`/`Resumed`), ödeme alınamadı (`PaymentFailed`, fatura başına), abonelik bitti (`Canceled`); anahtar abonelik + `Revision`, sıralama yok; tekrar yayın, plan değişikliği ve askıya alma e-posta üretmez.
- **Eş zamanlı yazma:** kaydetmede yarışı kaybeden command (concurrency, unique, FK) 409 `Data.Conflict` alır. Düzenlenen
  aggregate'ler `Version` taşır; istemci yüklediği değeri geri gönderir (ör. Catalog `ContentItem`).
- **Audit:** `IAuditedCommand` işaretli başarılı command'lar aynı transaction'da
  `AuditLogEntries`'e yazılır (`AuditBehavior`, tablo `ServiceDbContext`'te).
- **JWT:** ES256, `typ: at+jwt`, 15 dk; servisler Auth'un JWKS'ini `Authority` üzerinden bulur,
  başka algoritma veya tip kabul edilmez. Claim adları olduğu gibi (`sub`, `role`, `permission`).
  İmza anahtarları `Auth:Tokens` altında, asla loglanmaz; rotasyon adımları ARCHITECTURE §12.3.1.
- **Lisans kontrolü:** proje canlıya alınmayacak (portföy/bitirme projesi); açık kaynak veya ticari olmayan kullanımda
  ücretsiz paketler serbest, bu kullanım için ödeme isteyenler değil. Yeni bir paket eklemeden önce lisansını kontrol et.
