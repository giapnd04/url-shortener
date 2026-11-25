# ADR-006: Ghi click analytics đồng bộ hay bất đồng bộ

## Status

Proposed (bản nháp, cần chủ repo đọc lại và chuyển sang Accepted)

## Context

Mỗi lần redirect trả 302 là một click cần ghi vào click_events (US-11). Bản đầu tiên ghi đồng bộ trong chính request redirect. Benchmark chế độ E (Redis warm, analytics đồng bộ) so với B (Redis warm, tắt analytics) cho thấy:
- RPS giảm 67,7% (973,6 so với 3017,2).
- p50 tăng 367,8% (19,60 so với 4,19 ms).
- p95 tăng 33,4%.
- Số DB query mỗi request tăng từ 0,136 lên 1,13.

E còn chậm hơn cả A (không cache, không analytics). Như vậy cache gần như mất tác dụng khi mỗi redirect phải chờ một INSERT.

Câu hỏi: mất vài click khi app crash có chấp nhận được không? Nếu là dữ liệu tính tiền thì sao?

## Decision

Mặc định ghi bất đồng bộ (Analytics:Mode=Async, US-13). Vẫn giữ Sync làm tuỳ chọn.

- Redirect tạo ClickEvent rồi gọi ClickQueue.TryEnqueue. ClickQueue là một Channel<ClickEvent> có giới hạn (mặc định 10.000 phần tử, một reader, nhiều writer). Thao tác này không bao giờ chờ.
- Hàng đợi đầy (DB chậm hơn traffic trong một lúc) thì click bị bỏ và được đếm (Dropped). Redirect không bao giờ chờ analytics. Channel dùng FullMode=Wait cùng TryWrite, để biết được lúc nào bị bỏ; DropWrite thì báo thành công trong khi lặng lẽ vứt phần tử.
- ClickWriterService (BackgroundService) đọc theo lô: tối đa 500 dòng, hoặc những gì tới được trong 1 giây, và ghi bằng một lần SaveChanges.
- Khi tắt app: StopAsync đóng hàng đợi, worker ghi nốt những gì còn lại rồi mới dừng (graceful shutdown).
- Lô ghi lỗi thì log Error và bỏ lô đó, không retry mãi, để một dòng lỗi không làm tắc cả analytics.
- Đảm bảo giao nhận là at-most-once: crash hoặc kill -9 thì mất những click đang nằm trong hàng đợi. Với thống kê click, mất vài click là chấp nhận được. Nếu là dữ liệu tính tiền thì không: khi đó phải dùng outbox (ghi event cùng transaction với dữ liệu nghiệp vụ) hoặc một message broker có xác nhận.
- Bộ đếm enqueued, dropped, written, failed xem được ở /_diag/analytics, chỉ bật khi đo.

Chế độ F của benchmark (Redis warm, analytics bất đồng bộ) đã có trong runner nhưng chưa đo. Kỳ vọng là gần B, nhưng chưa có số nên không ghi số nào ở đây.

## Alternatives Considered

### Option 1: Ghi đồng bộ trong request (bản US-11, vẫn giữ làm Mode=Sync)
- Ưu điểm: không mất click nào, đơn giản, số thống kê có ngay.
- Nhược điểm: theo số đo ở trên, đây là chi phí lớn nhất của cả hệ thống.

### Option 2: Redis INCR rồi flush định kỳ xuống SQL
- Ưu điểm: rất nhanh, và chia sẻ được giữa nhiều instance.
- Nhược điểm: Redis chết là mất số đếm. Chỉ lưu được bộ đếm, không lưu được referrer hay trình duyệt. Thêm một nguồn sự thật thứ hai.

### Option 3: Message broker (RabbitMQ, Kafka)
- Ưu điểm: bền, không mất event khi app crash, nhiều consumer.
- Nhược điểm: thêm một hạ tầng phải vận hành. Quá mức so với yêu cầu thống kê click ở quy mô này. Đáng dùng khi event phải sống qua được crash, hoặc khi nhiều service khác cần nghe event.

## Consequences

### Positive
- Redirect không còn chờ DB ghi. Ghi theo lô giảm số round-trip.

### Negative
- Số thống kê trễ tối đa khoảng FlushInterval (1 giây) cộng thời gian ghi.
- Có thể mất click khi crash, và mất click khi hàng đợi đầy. Cả hai đều được đếm, trừ trường hợp crash.
- Mỗi instance có hàng đợi riêng trong bộ nhớ.

## Related Documents

- benchmarks/REPORT.md
- docs/02-database-design.md
- docs/03-api-contract.md
