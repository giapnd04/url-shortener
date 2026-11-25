# Các bug đã gặp và cách sửa

Chỉ ghi bug có thật, đã xảy ra trong project này hoặc trong OrderFlow. Mỗi mục ghi bốn ý: triệu chứng, nguyên nhân, cách sửa, và cách chứng minh đã sửa. Mục đầu tiên là bug còn mở.

## Còn mở

### Redis kết nối lại bị kẹt sau khi container khởi động lại (test không ổn định)

- Triệu chứng: RedisOutageTests thỉnh thoảng fail với thông báo "API did not start using Redis again after it came back". Chạy cả bộ integration 10 lần liên tiếp thì fail 2 lần. Chạy riêng test này thêm 26 lần thì fail 1 lần. Tổng cộng 3 trên 36 lần chạy.
- Quan sát: khi bị kẹt, multiplexer của API cứ mở được TCP rồi bị đóng ngay trong lúc handshake (SocketClosed, ReadEndOfStream, last: ECHO), lặp lại suốt hơn 30 giây. Cùng lúc đó, client Redis riêng của test kết nối bình thường qua cùng cổng. Khi không bị kẹt, API dùng lại cache trong 0 đến 1,5 giây sau khi Redis lên lại (đo 20 lần).
- Giả thuyết, chưa chứng minh: lớp chuyển cổng của Docker Desktop trên Windows trong khoảng một giây container đang khởi động, cộng với một trạng thái kết nối lại của StackExchange.Redis. Trong compose, API nói chuyện với Redis qua mạng Docker (redis:6379), không qua cổng của máy host, nên chưa chắc production gặp lỗi này.
- Hướng sửa nếu tái hiện được: "force reconnect", nghĩa là nếu lỗi kéo dài quá N giây thì tạo multiplexer mới. Chưa làm, vì chưa tái hiện được theo ý muốn nên không chứng minh được bản sửa có tác dụng.
- Ảnh hưởng hiện tại: redirect vẫn đúng (đi SQL), chỉ chậm hơn cho tới khi kết nối lại được.

## Đã sửa, URL Shortener

### Healthcheck của container api không bao giờ healthy

- Triệu chứng: docker compose up --wait báo "container url-shortener-api-1 is unhealthy", trong khi /health/ready trả 200 khi gọi từ ngoài.
- Nguyên nhân: healthcheck dùng CMD-SHELL, tức chạy qua /bin/sh. Trong image aspnet, /bin/sh là dash, không có /dev/tcp. Docker inspect ghi "cannot create /dev/tcp/127.0.0.1/8080: Directory nonexistent". Lỗi có từ Ngày 15 nhưng không ai thấy, vì lúc đó chưa chạy thử compose.
- Sửa: chạy bash trực tiếp (CMD, bash, -c).
- Chứng minh: container chuyển sang healthy, các lần chạy benchmark sau đó đều qua bước --wait.

### Cổng 8080 trên máy bị chiếm

- Triệu chứng: "bind: Only one usage of each socket address".
- Nguyên nhân: một process khác (AgentService) đang nghe cổng 8080 trên máy.
- Sửa: cổng host đọc từ biến API_PORT, benchmark dùng 18080. Không tắt process của người khác.

### Database không được tạo khi chạy compose trên máy sạch

- Triệu chứng (phát hiện khi đọc code lúc lập kế hoạch Ngày 23): migration chỉ chạy trong Development (DevSeed), nên compose trên máy sạch có /health/ready trả 503 mãi, container api không bao giờ healthy.
- Sửa: Bootstrap chạy migration khi Database:MigrateOnStartup=true (compose bật sẵn), có retry, và tạo API client từ BOOTSTRAP_API_KEY.
- Chứng minh: compose chạy với project name mới (volume trống) thì cả ba container healthy. Log có dòng "Database migrations applied" và "Created bootstrap API client". Tạo link, redirect 302 và xem stats đều chạy được.

### Test Redis timeout vì localhost trên Windows

- Triệu chứng: outage test luôn fail. Log có "UnableToConnect on localhost:49792" ngay từ trước khi dừng Redis.
- Nguyên nhân: trên Windows, localhost được phân giải thành ::1 (IPv6) trước. ConnectTimeout của app là 1 giây, nên hết giờ trước khi client chuyển sang thử IPv4. Client của test dùng timeout mặc định (5 giây) nên vẫn kết nối được, làm lỗi khó thấy.
- Sửa: test dùng 127.0.0.1.

### Redirect mất 1 đến 4 giây khi Redis chết

- Triệu chứng: thử tay trên compose, dừng Redis thì mỗi redirect mất 1 đến 4 giây, dù vẫn trả 302.
- Nguyên nhân: khi mất kết nối, StackExchange.Redis giữ lệnh trong backlog, chờ lần kết nối lại tiếp theo.
- Sửa: BacklogPolicy.FailFast, cộng với timeout 250 ms và tự kết nối lại mỗi giây.
- Chứng minh: test đo 5 redirect khi Redis tắt phải xong dưới 1,5 giây. Tạm bỏ FailFast thì test fail với 10 giây, có FailFast thì pass.

### Chế độ D của benchmark: Redis "tắt" nhưng hit ratio vẫn 0,16

- Triệu chứng: lượt chạy thử chế độ D (Redis bị dừng) báo hit ratio 0,16 và 0,84 DB query mỗi request, trong khi đúng ra phải là 0 và 1.
- Nguyên nhân: docker compose run k6 khởi động cả dependency của API, nên lặng lẽ bật lại Redis vừa bị script dừng.
- Sửa: bỏ depends_on của service k6, chạy bằng compose run --no-deps.
- Chứng minh: chạy lại thì hit ratio 0, đúng 1 DB query mỗi request. Số chính thức của D trong REPORT.md cũng vậy.

### Runner benchmark tự chặn chính nó

- Triệu chứng: run-all.ps1 dừng ở chế độ B với lỗi "Other containers are running: cranky_satoshi".
- Nguyên nhân: cuối mỗi chế độ, runner chạy docker run --rm grafana/k6 version để ghi phiên bản k6. Container tạm này chưa xoá xong thì bước kiểm tra container lạ của chế độ sau đã chạy.
- Sửa: đặt tên url-shortener-k6-version cho container tạm, để bước kiểm tra nhận ra nó là của mình. Thay đổi được ghi ở harnessChanges trong file kết quả.

### Test execution plan nhận về null

- Triệu chứng: test SHOWPLAN_XML cho truy vấn thống kê nhận về plan null.
- Nguyên nhân: SqlCommand có parameter được gửi qua sp_executesql. Estimated plan của batch đó không chứa câu lệnh bên trong.
- Sửa: khai báo biến bằng DECLARE ngay trong batch, chạy đúng câu SQL đó, rồi đọc mọi result set.
- Chứng minh: test thấy Index Seek trên IX_click_events_link_id_clicked_at.

### Lỗi parse PowerShell "$Runs:"

- Triệu chứng: script runner lỗi ngay ở dòng in tiến độ.
- Nguyên nhân: trong chuỗi PowerShell, "$Runs:" bị hiểu là biến có scope hoặc drive (giống $env:).
- Sửa: viết ${Runs}.

## Đã sửa, OrderFlow (repo OverFlow)

### CreateCustomer ném NotImplementedException (commit f5f34f9)

- Triệu chứng: gọi tạo customer qua handler thì nổ NotImplementedException, dù CustomerRepository có hàm AddAsync chạy đúng.
- Nguyên nhân: lớp có hai hàm. Một là public Task AddAsync(...) chứa code thật. Hai là ICustomerRepository.AddAsync(...) trả Task<bool>, là explicit interface implementation do IDE tự sinh, chỉ có throw new NotImplementedException(). Handler gọi qua interface nên luôn vào hàm thứ hai. Còn ai gọi thẳng bằng kiểu lớp thì vào hàm thứ nhất, nên đọc code lướt qua sẽ thấy "có chạy".
- Sửa: chỉ giữ một hàm public Task<bool> AddAsync khớp với interface, xoá bản explicit.
- Bài học: khi IDE sinh explicit interface member, phải xoá hoặc cài ngay. Unit test nên gọi qua interface.

### ProcessPayment ghi nửa chừng (OF-03, commit 7a78a34)

- Triệu chứng: lần ghi thứ hai của ProcessPayment lỗi thì payment đã được lưu, nhưng order và stock chưa được cập nhật.
- Nguyên nhân: handler gọi SaveChanges hai lần mà không có transaction bao ngoài.
- Sửa: bọc cả use case trong ITransactionRunner (ADR-006 của OrderFlow).
- Chứng minh: ProcessPaymentAtomicityTests dùng FailOnSaveChangesInterceptor làm lần lưu thứ hai lỗi, rồi kiểm tra không có payment nào, order vẫn PendingPayment, stock và reserved giữ nguyên.

### Thanh toán trùng và thanh toán vượt số tiền (commit 7f2221e)

- Nguyên nhân: ProviderTransactionId cho phép null, nên chống trùng chỉ chặn được một phần.
- Sửa: bắt buộc ProviderTransactionId, thêm kiểm tra chống trùng và chống trả vượt số tiền (ADR-008 của OrderFlow), có PaymentIdempotencyTests.

## Cách debug đã dùng (tóm tắt)

- Đi từ tổng quan xuống chi tiết: request thất bại thì xem status code, rồi log có correlation id, rồi exception, rồi state của dependency (docker inspect, trạng thái kết nối Redis trong message của exception).
- Với lỗi chỉ thỉnh thoảng mới xảy ra: chạy lặp nhiều lần và đếm trước khi sửa. Đo xem "chậm" hay "kẹt" trước khi đổi timeout.
- Chứng minh bản sửa bằng cách tạm gỡ nó ra và xem test fail, như với FailFast.
