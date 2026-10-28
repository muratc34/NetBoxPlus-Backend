# CLAUDE.md — NetBoxPlus-Backend (v2)

NetBoxPlus — Netflix benzeri video/abonelik platformu. Bu repo tüm backend'in monorepo'su: .NET 10
mikroservisler, servis başına Clean Architecture, ortak kod `src/BuildingBlocks/` altında.
Her geliştirmeden sonra bu dosya ve ilgili doküman güncellenir.

- Kararlar ve gerekçeleri: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
- Aktif faz ve iş dökümü: [docs/PHASE-0.md](docs/PHASE-0.md)

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
çalışmayan bir proje çıkış kodu 8 ile başarısız sayılır — bu bilerek açık. Boş test projeleri
dolana kadar solution genelinde test bu yüzden başarısız olur; proje bazında çalıştır.

## Çalışma kuralları

- Kullanıcıyla Türkçe konuşulur; kod, identifier'lar ve commit mesajları İngilizce.
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
- **Validation:** `IValidator<T>` senkron ve I/O yapmaz (zorunlu alan, uzunluk, format). Veritabanı
  gerektiren kurallar handler'da. Validator hatası exception değil `ValidationError`.
- **Contract'lar:** `*.Contracts` içinde düz record; işaret arayüzü yok, bağımlılık yok, tipler
  yeniden adlandırılmaz (mesaj tipi adı tip adından gelir).
- **Zaman:** `DateTime.UtcNow` doğrudan çağrılmaz, `TimeProvider` kullanılır.
- **ID'ler:** düz `Guid`, `Guid.CreateVersion7()`.
- **API:** Minimal API. Her uygulama `builder.AddServiceDefaults()` → `app.UseServiceDefaults()` →
  (auth) → endpoint'ler → `app.MapDefaultEndpoints()`. Endpoint'ler `result.ToHttpResult()` döner;
  hatalar RFC 9457 problem details. Yetki `RequirePermission("servis:kaynak:eylem")`.
- **Loglama:** Serilog yok; .NET `ILogger` + OpenTelemetry. `[LoggerMessage]` ile structured log,
  string interpolation yok; token/şifre/kart/e-posta loglanmaz. Hassas parametreler
  `[PersonalData]` / `[SecretData]` ile işaretlenir (projede `Microsoft.Extensions.Telemetry.Abstractions`
  doğrudan referanslı olmalı).
- **JWT:** asimetrik imza, servisler Auth'un JWKS'i ile doğrular; claim adları olduğu gibi (`sub`,
  `permission`).
- **Lisans kontrolü:** MediatR, AutoMapper, MassTransit v9, FluentAssertions v8 gibi ticari lisansa
  geçmiş paketler kullanılmaz. Yeni bir paket eklemeden önce lisansını kontrol et.
