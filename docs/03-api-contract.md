# 03. API Contract

Nguồn: mục 10 của requirement. Đây là hợp đồng cho v0.1 trở đi; endpoint stats thuộc v1.0.

## Quy ước chung

- Endpoint quản lý nằm dưới /api/links, bắt buộc header X-Api-Key. Thiếu hoặc sai key trả 401.
- Client chỉ thấy link của chính mình. Truy cập link của client khác trả 404, không trả 403, để không lộ việc code đó tồn tại.
- Lỗi trả về dạng ProblemDetails (RFC 9457), content type application/problem+json. Lỗi validation dùng ValidationProblemDetails có trường errors.
- Thời gian trong request và response là ISO 8601 UTC, ví dụ 2026-11-20T00:00:00Z.
- Danh sách có phân trang: page bắt đầu từ 1, pageSize mặc định 20, tối đa 100.

## Endpoint

POST /api/links

- Auth: API key.
- Body: { "url": "https://example.com/a/b", "expiresAt": "2026-12-31T00:00:00Z" }. expiresAt tuỳ chọn, phải ở tương lai.
- 201 Created, header Location: /api/links/{code}, body { code, shortUrl, targetUrl, createdAt, expiresAt }.
- 400 khi URL không hợp lệ: không phải http/https tuyệt đối, dài quá 2048 ký tự, scheme javascript: hoặc data:, hoặc trỏ về chính domain của service.
- 401 khi thiếu hoặc sai key.

GET /api/links?page=&pageSize=

- Auth: API key.
- 200, body { items: [...], page, pageSize, totalCount }. Sắp xếp createdAt giảm dần.

GET /api/links/{code}

- Auth: API key.
- 200 với chi tiết link, gồm status (active, disabled, expired).
- 404 khi không có hoặc thuộc client khác.

PATCH /api/links/{code}

- Auth: API key.
- Body: { "url": "...", "expiresAt": "..." }, mỗi trường tuỳ chọn.
- 200 với link sau khi sửa. Từ v0.2: commit DB xong mới xoá cache.
- 400, 404.

POST /api/links/{code}/disable

- Auth: API key.
- 204. Gọi lại lần nữa vẫn 204 (idempotent). Từ v0.2: xoá cache sau commit.
- 404.

GET /api/links/{code}/stats?from=&to= (v1.0)

- Auth: API key.
- 200, body { total, byDay: [{ day, clicks }] }. Ngày tính theo UTC.
- 400 khi from > to hoặc khoảng quá dài; 404.

GET /{code}

- Public.
- 302 Found, header Location là target URL. Lý do chọn 302: docs/adr/ADR-002-redirect-status-code.md.
- 404 khi code không tồn tại.
- 410 Gone khi link đã disabled hoặc đã hết hạn.

GET /health/live

- Public. 200 khi process còn sống. Không kiểm tra dependency.

GET /health/ready

- Public. 200 khi SQL Server truy cập được, 503 khi không. Từ v0.2: Redis lỗi chỉ làm trạng thái Degraded, vẫn 200, vì redirect vẫn chạy được qua SQL.

## Route constraint cho GET /{code}

Route /{code} bắt mọi đường dẫn một đoạn nên có thể đè lên /health hay /api nếu không giới hạn. Ràng buộc dự kiến:

- Chỉ ký tự Base62 [0-9A-Za-z], độ dài đúng bằng độ dài code chọn ở ADR-001 (nháp: 7).
- Viết dạng regex constraint, ví dụ "/{code:regex(^[0-9A-Za-z]{{7}}$)}".

Lợi ích: request rác như /favicon.ico, /wp-admin hay chuỗi dài bị trả 404 ngay ở routing, không chạm DB và sau này không chạm Redis.

## Ví dụ lỗi

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "url": ["Only absolute http and https URLs are allowed."]
  }
}
```
