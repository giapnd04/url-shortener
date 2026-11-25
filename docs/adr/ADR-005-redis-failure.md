# ADR-005: Hành vi khi Redis lỗi

## Status

Proposed (bản nháp, cần chủ repo đọc lại và chuyển sang Accepted)

## Context

Redis là một thành phần có thể chết bất kỳ lúc nào: container restart, hết bộ nhớ, lỗi mạng. Redirect là chức năng chính, không được hỏng chỉ vì cache hỏng.

Hai câu hỏi:
- Nếu Redis chết đúng lúc traffic cao, DB có chịu nổi không?
- Timeout bao nhiêu là hợp lý so với latency DB đo được?

## Decision

Fail-open: lỗi Redis được coi như cache miss, request đọc thẳng SQL, không trả lỗi cho client.

Cấu hình StackExchange.Redis (InfrastructureSetup):
- AbortOnConnectFail=false: app khởi động được kể cả khi Redis chưa lên.
- ConnectTimeout 1000 ms, SyncTimeout và AsyncTimeout 250 ms: so với p50 của SQL khoảng 9,5 ms, chờ Redis lâu hơn vài trăm ms là vô nghĩa.
- BacklogPolicy.FailFast: khi đang mất kết nối, lệnh thất bại ngay chứ không nằm hàng đợi chờ kết nối lại. Trước khi đặt cấu hình này, thử tay trên compose cho thấy mỗi redirect mất 1–4 giây khi Redis tắt. Có integration test chứng minh: bỏ FailFast thì 5 redirect mất 10 giây và test fail.
- ReconnectRetryPolicy là LinearRetry 1 giây: Redis lên lại thì app tự dùng cache, không cần restart.
- Log Warning tối đa 10 giây một lần, để log không bị ngập khi mọi request đều lỗi Redis.
- /health/ready báo Degraded, vẫn HTTP 200. Orchestrator không rút instance khỏi traffic chỉ vì cache chết.

Số đo, chế độ D (Redis dừng trước warm-up) so với A (không cache): lỗi 0%, p50 9,80 so với 9,48 ms, nhưng p95 56,27 so với 30,67 ms (+83,5%) và RPS 1311,4 so với 1712,9 (−23,4%). Trả lời câu hỏi "DB có chịu nổi không": ở tải benchmark thì có. Mọi request đều đi SQL với 0% lỗi. Nhưng chế độ D chậm hơn cả lúc không có cache.

## Alternatives Considered

### Option 1: Fail-closed (trả 503 khi Redis lỗi)

- Ưu điểm: bảo vệ DB khỏi bị dồn tải đột ngột.
- Nhược điểm: biến cache thành điểm chết của chức năng chính. Không hợp với một thành phần mà theo định nghĩa có thể mất.

### Option 2: Circuit breaker

Sau N lần lỗi liên tiếp thì bỏ qua Redis hoàn toàn một khoảng thời gian, rồi thử lại.

- Ưu điểm: mất chi phí tạo exception cho mỗi request trong lúc Redis chết. Đây có thể là nguyên nhân chế độ D chậm hơn A (giả thuyết, chưa đo).
- Nhược điểm: thêm trạng thái và thêm tham số phải chỉnh. Chưa làm; đây là cải tiến đầu tiên nếu D cần nhanh hơn.

## Consequences

### Positive
- Redis chết không làm hỏng redirect. Có test stop/start container thật (RedisOutageTests).

### Negative
- Khi Redis chết, toàn bộ tải dồn xuống SQL, và theo số đo còn chậm hơn chạy không cache. Ở quy mô lớn hơn phải có circuit breaker và đo lại xem DB có chịu nổi không.

## Related Documents

- benchmarks/REPORT.md
- docs/adr/ADR-003-caching-pattern.md
