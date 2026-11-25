# ADR-003: Caching pattern cho redirect

## Status

Proposed (bản nháp, cần chủ repo đọc lại và chuyển sang Accepted)

## Context

GET /{code} là thao tác chiếm gần hết traffic: đọc nhiều, ghi rất ít, và một link ít khi đổi sau khi tạo. Phân bố truy cập lệch: trong benchmark, 80% request rơi vào 20% số link. SQL Server là nguồn sự thật; Redis chỉ là bản sao có thể mất bất kỳ lúc nào.

Câu hỏi: với dữ liệu đọc nhiều, ghi ít và ít khi đổi như vậy, pattern nào đơn giản và an toàn nhất?

## Decision

Dùng cache-aside, cài thành Decorator CachedRedirectReader bọc SqlRedirectReader (src/UrlShortener.Infrastructure/Links/RedirectReaders.cs).

- Đọc: GET Redis. Hit thì trả luôn. Miss thì đọc SQL, rồi SET với TTL = min(1 giờ, thời gian còn lại của link).
- Ghi: commit SQL trước, sau đó xoá key (chi tiết ở ADR-004).
- Code không tồn tại không được cache (negative caching là US-06, để giai đoạn 2).
- Key link:v2:{code}, value JSON gọn {i, t, s, e, u}.

Số đo (benchmarks/REPORT.md, trung vị 3 lần chạy): B (Redis đã warm) so với A (không cache) tăng RPS 76,1% (3017,2 so với 1712,9), giảm p50 55,8% (4,19 so với 9,48 ms), giảm số DB query mỗi request từ 1 xuống 0,136, với hit ratio 0,8642. p95 chỉ giảm 16,1% và p99 giảm 6,1%.

## Alternatives Considered

### Option 1: Write-through

Ghi vào DB và cache trong cùng một thao tác.

- Ưu điểm: cache luôn mới, đọc lần đầu cũng hit.
- Nhược điểm: mỗi lần ghi chậm hơn và có hai nơi phải cùng thành công. Cache chứa cả những link không ai đọc. Không cache lại được những link đã bị Redis evict.

### Option 2: Write-behind (write-back)

Ghi vào cache trước, ghi xuống DB sau theo lô.

- Ưu điểm: ghi rất nhanh.
- Nhược điểm: Redis chết trước khi flush là mất dữ liệu. Với dữ liệu mà SQL phải là nguồn sự thật thì không chấp nhận được.

### Option 3: Read-through

Thư viện hoặc cache server tự đọc DB khi miss.

- Ưu điểm: code gọi đơn giản.
- Nhược điểm: Redis không tự làm read-through. Phải thêm một lớp thư viện, và logic miss, TTL, lỗi bị giấu đi trong khi chính những thứ đó cần đo và cần test.

### Option 4: Cache trong bộ nhớ (IMemoryCache hoặc LruCache tự viết)

- Ưu điểm: không thêm hop mạng, không thêm hạ tầng.
- Nhược điểm: mỗi instance có một bản riêng. Xoá cache ở instance này không tới instance kia, và cache mất khi restart. Vẫn giữ làm Cache:Provider=Memory (US-16) để so sánh.

## Consequences

### Positive
- Redis chết thì app vẫn chạy bằng SQL (ADR-005).
- Chỉ link thật sự được đọc mới chiếm bộ nhớ, rất hợp với traffic 80/20.
- Logic nằm trong code của mình, có test cho hit, miss, TTL và xoá key.

### Negative
- Lần đọc đầu và mọi code không tồn tại luôn đi SQL. Đuôi latency (p95, p99) cải thiện ít.
- Có cửa sổ dữ liệu cũ khi xoá key thất bại, giới hạn bởi TTL (ADR-004).
- Khi cache còn lạnh, có thể tệ hơn không cache. Lần chạy đầu của B (hit ratio 0,7328) có p95 33,74 ms, trong khi A là 30,67 ms.

## Related Documents

- benchmarks/REPORT.md
- docs/02-database-design.md
- docs/adr/ADR-004-cache-invalidation.md
- docs/adr/ADR-005-redis-failure.md
