# Báo cáo hiệu năng redirect (US-10)

Trạng thái: CHƯA ĐO. Đây mới là khung báo cáo, chưa có con số nào. Mọi chỗ ghi "chưa đo" chỉ được điền bằng số lấy từ các file benchmarks/results/*.json do benchmarks/run-all.ps1 tạo ra. Không ước lượng, không làm tròn đẹp, không lấy số từ lần chạy thử (file có đuôi -dirty).

## Cách đo

- Một lệnh đo cho tất cả: ./benchmarks/run-all.ps1 -Commit <commit>. Baseline SQL đo trên commit 15683bb (chưa có cache), các chế độ A, B, D, E đo trên commit ghi trong từng file kết quả.
- Dataset: 100.000 link sinh tất định (benchmarks/sql/seed.sql). code(n) = Base62(n × 1000000007 mod 62^7).
- Tải: k6 với 20 VU cố định, đo 60 giây sau 20 giây warm-up. Phần warm-up không tính vào số liệu và không tính vào số DB query.
- Phân bố request: 80% vào link nóng (20% số link), 15% vào link nguội, 5% vào code không tồn tại (trả 404).
- Mỗi chế độ chạy 3 lần. Báo cáo lấy trung vị và ghi độ lệch giữa max và min so với trung vị.
- Giới hạn tài nguyên: API 2 CPU / 1 GB, SQL Server 2 CPU / 2 GB. k6 chạy cùng máy, cùng Docker network với API, nên tranh CPU với API. Đây là giới hạn của phép đo.
- Script từ chối đo khi máy còn container khác đang chạy.

## Các chế độ

- Baseline (commit 15683bb): chỉ có SQL, code chưa có cache.
- A: code hiện tại, Cache:Provider=None, tắt analytics.
- B: Redis cache-aside đã warm, tắt analytics.
- D: Redis bị dừng trước warm-up, mọi request đọc thẳng SQL (fail-open).
- E: như B nhưng bật analytics, nên mỗi lần trả 302 có thêm một INSERT.
- C (cache lạnh hoặc TTL hết hạn) để giai đoạn 2.

## Kết quả

Mỗi chế độ ghi: p50, p95, p99 (ms), RPS, số DB query mỗi request, hit ratio, tỉ lệ lỗi, và độ lệch giữa 3 lần chạy.

- Baseline: chưa đo.
- A: chưa đo.
- B: chưa đo.
- D: chưa đo.
- E: chưa đo.

Biểu đồ p95 theo chế độ sẽ được thêm khi có số (mermaid xychart, GitHub hiển thị được).

## Câu hỏi phân tích (trả lời bằng số của chính mình sau khi đo)

- Lợi ích chính của cache thể hiện ở latency hay ở số DB query? Vì sao?
- Hit ratio bao nhiêu thì cache bắt đầu có lợi? Nếu traffic phân bố đều, không có key nóng, thì sao?
- Chế độ D chậm hơn A bao nhiêu, tức Redis chết thì tốn thêm bao nhiêu? Trước khi bật BacklogPolicy.FailFast, phép thử tay trên compose cho thấy mỗi redirect mất 1–4 giây khi Redis tắt. Đây là quan sát định tính, không phải số benchmark.
- Analytics đồng bộ (E so với B) tốn thêm bao nhiêu ở p95?
- Baseline và A có khác nhau không? Nếu có thì đó là chi phí của lớp trừu tượng, vì code đã đổi dù không có cache.

## Vì sao chọn cache-aside

Cache-aside là app tự đọc cache, miss thì đọc DB rồi ghi vào cache; khi ghi thì commit DB rồi xoá key. Có ba lý do chọn cách này:
- Redis chỉ là bản sao có thể mất bất kỳ lúc nào. Redis chết thì app vẫn đọc DB được (fail-open).
- Chỉ link nào thật sự được đọc mới vào cache. Với phân bố 80/20 thì phần lớn bộ nhớ dành cho link nóng.
- Logic nằm trong code của mình và test được (CachedRedirectReader là một Decorator).

Hai cách khác, ở mức khái niệm:
- Write-through: ghi DB và cache cùng lúc. Cache luôn mới, nhưng mỗi lần ghi chậm hơn, và cache chứa cả link không ai đọc.
- Write-behind: ghi cache trước, ghi DB sau theo lô. Ghi rất nhanh, nhưng Redis chết là mất dữ liệu. Không chấp nhận được khi SQL phải là nguồn sự thật.

## Khi nào cache không giúp

Phần này phải dựa trên số đo của project. Các giả thuyết cần kiểm chứng:
- Code không tồn tại (5% traffic) luôn miss và luôn đi SQL. Negative caching (US-06) để giai đoạn 2.
- Traffic đều, không có key nóng: hit ratio thấp, cache chỉ thêm một round-trip mạng.
- DB đã đủ nhanh: index seek trên 100k dòng vốn đã nhanh, nên latency có thể không giảm nhiều, chỉ số DB query giảm.
- Dữ liệu phải luôn mới: cache chấp nhận dữ liệu cũ tối đa bằng TTL nếu xoá key thất bại.
- Ghi nhiều hơn đọc: mỗi lần ghi phải xoá key, nên cache bị làm rỗng liên tục.

## Checklist mục 13 (đánh dấu khi đã có số)

- Commit hash, CPU, RAM, OS, giới hạn Docker, phiên bản SQL Server và Redis: tự ghi trong từng file JSON.
- Kích thước dataset, index, seed, tỉ lệ nóng/nguội/không tồn tại: ghi trong JSON và ở phần Cách đo.
- Số VU, thời lượng, warm-up, TTL, analytics bật hay tắt, số lần chạy: ghi trong JSON.
- k6 và API cùng máy: có, đã ghi là giới hạn.
