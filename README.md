# URL Shortener

URL shortener viết bằng ASP.NET Core 9 (Minimal API), EF Core 9 và SQL Server 2022. Redis sẽ được thêm sau, có chủ đích.

Giá trị của project không nằm ở tính năng rút gọn link mà ở quá trình đo có kiểm soát: dựng bản chỉ dùng SQL, đo baseline, thêm Redis cache-aside, xử lý khi Redis lỗi, rồi đo lại trong cùng điều kiện. Mọi con số hiệu năng trong repo phải có file kết quả gốc và commit đi kèm.

Trạng thái hiện tại: khung code base (Ngày 15). Chưa có endpoint nghiệp vụ.

## Problem

- Redirect là thao tác đọc chiếm phần lớn traffic; mỗi request đọc DB thì DB thành điểm nghẽn.
- Thêm cache sinh vấn đề mới: dữ liệu cũ sau khi sửa link, Redis chết làm hỏng redirect.
- Ghi thống kê ngay trong request redirect làm chậm chính thao tác quan trọng nhất.
- Không đo thì câu "Redis giúp nhanh hơn" chỉ là giả định.

## Scope

In scope: tạo link, redirect, quản lý link bằng API key; baseline SQL; Redis cache-aside (TTL, xoá cache khi sửa link, fail-open khi Redis lỗi); benchmark k6; click analytics theo ngày; Docker Compose, CI, structured logging.

Out of scope: giao diện web, đăng ký tài khoản, JWT/role, multi-region, CDN thật, Redis Cluster/Sentinel, Redis nâng cao (negative caching, stampede, distributed lock) để giai đoạn 2.

## Các mốc (git tag)

- v0.1-sql-baseline: tạo link, redirect, quản lý link, chưa có Redis; kết quả benchmark baseline gắn commit này.
- v0.2-redis-cache-aside: cache-aside, TTL, xoá cache khi sửa link.
- v0.3-resilience: Redis lỗi thì đọc thẳng SQL.
- v0.4-benchmark-compare: đo lại không cache, Redis warm, Redis down.
- v1.0: analytics, Docker, CI, tài liệu hoàn chỉnh.

## Cấu trúc

- src/UrlShortener.Api: Minimal API, endpoint, health check.
- src/UrlShortener.Infrastructure: truy cập SQL Server (EF Core), sau này là Redis.
- tests/UrlShortener.UnitTests và tests/UrlShortener.IntegrationTests.
- docs/: project definition, database design, API contract, ADR.

Vì sao chỉ 2 project mà không dùng Clean Architecture như OrderFlow: xem docs/adr/ADR-007-architecture-minimal-api.md.

## Chạy local

Cách 1, dùng LocalDB (Windows):

```powershell
dotnet run --project src/UrlShortener.Api
```

Rồi mở http://localhost:5100/health/live và http://localhost:5100/health/ready.

Cách 2, dùng Docker Compose:

```powershell
Copy-Item .env.example .env
docker compose up --build
```

Rồi mở http://localhost:8080/health/ready.

Chạy test:

```powershell
dotnet test UrlShortener.sln
```

## Tài liệu

- docs/01-project-definition.md
- docs/02-database-design.md
- docs/03-api-contract.md
- docs/adr/ADR-001-short-code-strategy.md
- docs/adr/ADR-002-redirect-status-code.md
- docs/adr/ADR-007-architecture-minimal-api.md

ADR-003 (caching pattern), ADR-004 (invalidation), ADR-005 (Redis lỗi), ADR-006 (analytics) sẽ viết khi tới phần tương ứng.
