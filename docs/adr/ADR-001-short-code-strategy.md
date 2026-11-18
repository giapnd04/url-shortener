# ADR-001: Chiến lược sinh short code

## Status

Proposed (bản nháp, cần chủ repo đọc lại, kiểm tra phép tính và chuyển sang Accepted)

## Context

Mỗi link cần một code ngắn, duy nhất, dùng trong URL công khai GET /{code}. Yêu cầu từ US-01:

- Ngắn để dễ chia sẻ.
- Duy nhất; unique index trên short_links.code là nơi quyết định cuối cùng.
- Không cho người ngoài đoán được số lượng link hay dò tuần tự link của người khác.
- Test được trường hợp trùng: bộ sinh code phải inject được để giả lập trùng.

Có ba phương án thường gặp.

## Decision

Chọn random Base62, độ dài 7 ký tự, sinh bằng CSPRNG (RandomNumberGenerator), dựa vào unique index để phát hiện trùng và retry có giới hạn.

Chi tiết:

- Bảng chữ cái Base62: 0-9, A-Z, a-z. Lấy mẫu bằng RandomNumberGenerator.GetInt32(62) cho từng ký tự để không bị lệch phân phối như khi lấy byte % 62.
- Bộ sinh code là interface IShortCodeGenerator, đăng ký qua DI; test thay bằng bản giả trả code định trước.
- Luồng tạo link: sinh code, INSERT. Nếu SQL báo vi phạm unique index (lỗi 2601 hoặc 2627) trên code thì sinh lại. Tối đa 5 lần thử; quá giới hạn thì trả lỗi có kiểm soát (503 ProblemDetails) và log Error, không lặp vô hạn.
- Không SELECT kiểm tra trùng trước khi INSERT: hai request đồng thời đều có thể thấy "chưa có" rồi cùng INSERT. Unique index mới là thứ nguyên tử.
- Cột code dùng collation Latin1_General_100_BIN2 (xem docs/02-database-design.md), nếu không thì abc và ABC bị coi là trùng.
- Cùng một URL tạo hai lần sẽ ra hai code khác nhau. Mỗi link có owner, expiry và thống kê riêng nên đây là hành vi mong muốn.

## Phép tính không gian code và xác suất trùng

- Số tổ hợp: 62^7 ≈ 3,52 × 10^12 (khoảng 3,5 nghìn tỷ).
- Xác suất một lần INSERT mới bị trùng khi đã có n link: xấp xỉ n / 62^7. Với n = 100.000 (dataset benchmark) là khoảng 2,8 × 10^-8, tức gần như không bao giờ.
- Xác suất có ít nhất một lần trùng trong cả quá trình tạo n link (bài toán ngày sinh): xấp xỉ n² / (2 × 62^7).
  - n = 100.000: khoảng 0,14%.
  - n = 1.000.000: khoảng 13%.
  - n ≈ 2,65 triệu: khoảng 63%.
- Kết luận: trùng chắc chắn sẽ xảy ra ở quy mô vài triệu link, nên retry là bắt buộc chứ không phải phòng hờ. Nhưng xác suất trùng 5 lần liên tiếp vẫn cực nhỏ; nếu xảy ra thì gần như chắc chắn là bug bộ sinh code hoặc không gian code sắp đầy, nên phải log Error.

## Alternatives Considered

### Option 1: Base62(identity)

Mã hoá id bigint identity sang Base62 (id 125 ra "21").

- Ưu điểm: không bao giờ trùng, không cần retry; code ngắn lúc đầu.
- Nhược điểm: lộ tổng số link và tốc độ tạo link; người ngoài dò tuần tự được mọi link (tăng id lên 1). Phải INSERT trước để có id rồi UPDATE code, tức 2 lệnh ghi, hoặc phải dùng sequence. Độ dài code thay đổi theo thời gian. Có thể che bằng cách hoán vị id (ví dụ nhân với số nguyên tố modulo) nhưng đó là che chứ không phải ngẫu nhiên.

### Option 2: Random Base62 7 ký tự + unique index + retry (chọn)

- Ưu điểm: không lộ số lượng, không dò tuần tự được; một lệnh INSERT; độ dài cố định nên route constraint đơn giản; test trùng dễ bằng generator giả.
- Nhược điểm: có thể trùng, phải có retry; code ngẫu nhiên làm index chèn vào vị trí rải rác (page split nhiều hơn so với giá trị tăng dần). Ở quy mô project này không đáng kể vì clustered key vẫn là id identity, chỉ index phụ trên code bị rải.

### Option 3: Hash(URL) cắt ngắn

Ví dụ SHA-256 của URL, mã hoá Base62, lấy 7 ký tự đầu.

- Ưu điểm: cùng URL ra cùng code (tự khử trùng lặp).
- Nhược điểm: cắt ngắn hash vẫn trùng như random, vẫn cần xử lý; khi hai URL khác nhau trùng code thì phải thêm salt hoặc đổi đoạn cắt, phức tạp hơn retry. Cùng URL ra cùng code lại sai với mô hình mỗi link có owner, expiry riêng. Ai biết URL thì đoán được code.

## Consequences

### Positive
- Không lộ thông tin kinh doanh qua code.
- Luồng tạo link chỉ một lệnh ghi.
- Có test rõ ràng cho trường hợp trùng (AC của US-01).

### Negative
- Phải xử lý lỗi unique violation đúng mã lỗi và retry.
- Quy mô vài chục triệu link thì cần tăng lên 8 ký tự; route constraint và cột varchar(16) đã chừa chỗ.

## Câu hỏi tự kiểm tra trước khi Accepted

- Vì sao byte % 62 làm lệch phân phối? Ký tự nào bị chọn nhiều hơn?
- Bắt lỗi trùng bằng DbUpdateException rồi kiểm tra SqlException.Number. Làm sao phân biệt trùng code với trùng một unique index khác?
- Nếu dùng System.Random thay CSPRNG thì rủi ro là gì với một service công khai?
- Khi nào nên đổi sang 8 ký tự, dựa trên số liệu nào?

## Related Documents

- docs/02-database-design.md
- docs/03-api-contract.md
- Requirement US-01, mục 11 ADR-001
