
# Production-ready qilish uchun keyingi bosqichlar (prioritet bilan)

Bu hujjat LiteAPI’ni production darajaga olib chiqish uchun amaliy roadmap. Prioritetlar P0 → P3: avval correctness/security, keyin reliability, observability va DX.

---

## P0 — Correctness & Security (eng birinchi)

### 1) Authorization metadata’ni real ishlaydigan qilish
- Route match natijasida topilgan `RouteDefinition.Metadata` ni `LiteHttpContext.RouteMetadata` ga propagate qilish.
- `AllowAnonymous / RequirePolicy / RequireRoles` kabi chain’lar amalda ishlashini tasdiqlash.
- Route metadata source bitta bo‘lsin: `RouteDefinition.Metadata` (yoki Router ichidagi metadata map) — ikkita parallel mexanizm bo‘lib ketmasin.

### 2) Wildcard route va static files’ni to‘g‘ri ishlatish
- `/{*path}` semantikasini router matching’da qo‘llab-quvvatlash (segmentlar soni teng bo‘lish sharti olib tashlanadi).
- Route precedence: aniq route > parametrli route > wildcard.
- Static file routing’ni `RouteAsync` oqimida ishlashini tekshirish.

### 3) Static file serving xavfsizligi (path traversal)
- URL decode + `..` segmentlarni normalize qilib, root’dan tashqariga chiqishni taqiqlash.
- Fayl yo‘li faqat `wwwroot` (yoki berilgan root) ichida bo‘lishi shart.
- Content-Type mapping minimal bo‘lsa ham, `application/octet-stream` fallback saqlansin.

### 4) Request body’ni faqat 1 marta o‘qish
- `HttpListenerRequest.InputStream` ni har parametr uchun qayta o‘qish o‘rniga body’ni bir marta buffer’lash.
- `[FromBody]` + bir nechta complex parametr holatlari uchun aniq qoidalar (misol: bir request’da faqat bitta body-model).

### 5) Error handling semantikasi va response izchilligi
- Handler exception → 500 (Internal Server Error).
- Parse/bind/validation xato → 400.
- Auth fail → 401, policy/role fail → 403.
- Default error payload: JSON (kamida `message`, `statusCode`, `traceId/requestId`).

---

## P1 — Reliability & Limits

### 1) Concurrency / DoS risklarini kamaytirish
- Har request uchun cheklanmagan `Task.Run` o‘rniga concurrency limit (masalan `SemaphoreSlim`).
- Long-running requestlar uchun timeout/cancellation strategiyasi.

### 2) Request/response limitlar
- Max body size (katta payload’larda memory portlashining oldini olish).
- Slow request (slowloris) va header limitlariga minimal himoya.

### 3) Rate limiting’ni barqarorlashtirish
- In-memory dictionary uchun eviction/cleanup (window o‘tgan keylarni tozalash).
- Key strategiyasi: per-IP, per-route, per-token (kerak bo‘lsa).
- Istiqbol: pluggable backend (Redis) opsiyasi.

### 4) Compression’ni ehtiyotkor qo‘llash
- Faqat mos content-type va minimal body size threshold.
- Already-compressed turlar (png/jpg/zip) ni siqmaslik.
- Double-compress holatini bloklash.

---

## P2 — Observability (log/health/metrics)

### 1) Strukturali logging
- Request-id (correlation id) generatsiya qilish va barcha log’larda olib yurish.
- Access log: method, path, status, duration.

### 2) Health check
- `/healthz` endpoint: minimal “OK”, versiya/build info (opsional).

### 3) Minimal metrics
- Request count, 4xx/5xx count, latency histogram (soddalashtirilgan).
- Keyingi bosqich: `System.Diagnostics.Activity` bilan tracing hook.

---

## P3 — Developer Experience (test, docs, CI)

### 1) Testlar
- Router matching precedence (static/param/wildcard).
- Auth metadata propagation (AllowAnonymous/RequireRoles/Policy).
- Body read-once + binder edge case’lar.
- Static files path traversal test.

### 2) OpenAPI’ni kengaytirish
- RequestBody + content-types.
- Response code’lar (200/201/400/401/403/404/500) minimal mapping.
- Auth security scheme’lar (Bearer/ApiKey) export.

### 3) CI/CD va packaging
- GitHub Actions (yoki boshqa CI): build + test + pack.
- SemVer + changelog.
- Release checklist (breaking changes, migration notes).

---

## Tavsiya etilgan ish tartibi (2–3 sprintga bo‘lib)

### Sprint 1 (P0)
- Authorization metadata propagation
- Wildcard/static files + path traversal fix
- Body read-once

### Sprint 2 (P1)
- Concurrency limit + request limits
- Rate limit cleanup
- Compression rules

### Sprint 3 (P2 + P3)
- Logging/health/metrics
- Test suite + CI
- OpenAPI improvements

