# 01. Project Definition

Nguồn: Project2_URL_Shortener_Requirements_Final.docx, phiên bản 2.0 (18/10/2026), cập nhật 26/10/2026.

## Overview

Dịch vụ rút gọn URL. Client (xác thực bằng API key) tạo link ngắn, quản lý link và xem thống kê. Khách truy cập GET /{code} được chuyển hướng tới URL gốc.

Project xây mới, repo riêng, độc lập với OrderFlow. Trọng tâm là đo lường: baseline SQL trước, Redis sau, đo lại cùng điều kiện.

## Goals

- G1: baseline hiệu năng tái lập được cho bản chỉ dùng SQL (script k6, dataset seed, file kết quả gắn commit).
- G2: Redis cache-aside đúng cách và đo được lợi ích (DB query mỗi request, p50/p95, RPS trước và sau).
- G3: cache vẫn đúng khi dữ liệu thay đổi (xoá cache khi sửa link) và khi Redis lỗi (fail-open về SQL).
- G4: thống kê truy cập theo ngày mà không phá latency redirect.
- G5: phân tích trade-off trung thực, kể cả khi cache không giúp.

## Phạm vi mốc v0.1-sql-baseline

Chỉ các requirement sau, không có bất kỳ code hay gói Redis nào:

- US-01 Tạo short link: validate URL, sinh code, retry có giới hạn khi trùng, collation phân biệt hoa thường.
- US-02 Redirect qua SQL: 302, 404, 410; route constraint; đúng 1 DB query mỗi redirect.
- US-03 Quản lý link bằng API key: header X-Api-Key, lưu SHA-256 của key, chỉ thấy link của mình.
- US-04 Measurement harness và baseline: seed 100k link, k6 có warm-up, phân bố 80/20, chạy 3 lần lấy trung vị.

## Các mốc sau

- v0.2: US-05 Redis cache-aside, US-07 xoá cache khi sửa link.
- v0.3: US-08 Redis lỗi và fallback.
- v0.4: US-10 đo lại và báo cáo so sánh.
- v1.0: US-11 click analytics, US-12 Docker, CI, logging, tài liệu.
- P1 nếu dư thời gian: US-13 async analytics, US-14 bảng tổng hợp, US-15 rate limit bằng Redis, US-16 in-memory cache Singleton + LRU.
- Giai đoạn 2: US-06 negative caching, US-09 stampede.

## Non-functional

- Correctness: SQL là nguồn sự thật; cache không bao giờ redirect tới link đã disabled quá staleness bound đã định nghĩa.
- Availability: Redis lỗi không làm redirect lỗi.
- Performance: không đặt ngưỡng tuyệt đối trước khi đo; yêu cầu là đo đúng phương pháp và báo cáo trung thực.
- Security: API key lưu hash; không redirect tới scheme nguy hiểm; không lưu IP thô.
- Observability: mỗi redirect log được kết quả cache và thời gian xử lý.
- Reproducibility: mọi benchmark chạy lại được từ repo.

## Công nghệ

- .NET 9, ASP.NET Core Minimal API.
- SQL Server 2022, EF Core 9.
- Redis 7 qua StackExchange.Redis (từ v0.2, dùng trực tiếp, không qua IDistributedCache).
- k6 cho benchmark.
- xUnit, Testcontainers (MsSql, sau này Redis).
- Docker Compose, GitHub Actions.
