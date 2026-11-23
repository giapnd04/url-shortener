# Design patterns dùng thật trong project

Tài liệu này chỉ ghi những pattern có code thật để chỉ vào, trong repo này (URL Shortener) hoặc trong OrderFlow. Mỗi mục trả lời ba câu: pattern là gì, nó nằm ở đâu, và vì sao dùng nó ở đó. Nguồn đọc: Refactoring.Guru (Creational, Structural, Behavioral).

## Creational: tạo đối tượng

### Singleton

Ý tưởng: cả ứng dụng chỉ có một instance của một lớp, ai cần thì dùng chung instance đó.

Trong URL Shortener có hai chỗ:

- IConnectionMultiplexer của StackExchange.Redis đăng ký AddSingleton trong src/UrlShortener.Infrastructure/InfrastructureSetup.cs. Lý do: multiplexer thread-safe, gom mọi lệnh Redis qua vài kết nối, và tạo mới rất tốn kém. Nếu tạo một cái cho mỗi request thì sẽ cạn kết nối.
- ILinkCache (Redis hoặc InMemoryLinkCache) cũng là Singleton. Với cache trong bộ nhớ thì bắt buộc phải vậy: nếu là Scoped, mỗi request nhận một cache rỗng, coi như không có cache. Test Cache_is_a_single_instance_for_the_whole_app kiểm tra điều này.

Singleton qua DI khác Singleton kiểu cổ điển (constructor private, static Instance, Lazy<T> hoặc lock để thread-safe). Em chọn DI vì vẫn thay được bằng bản giả khi test và không có trạng thái global ẩn. Nếu phải viết kiểu cổ điển thì dùng private static readonly Lazy<T> với LazyThreadSafetyMode.ExecutionAndPublication. Cách này tránh được lỗi hai thread cùng tạo instance mà không phải tự viết double-checked locking.

Điểm cần cảnh giác: Singleton giữ trạng thái chung nên phải thread-safe. LruCache dùng một lock cho mọi thao tác vì lý do này. Singleton cũng không được phụ thuộc vào service Scoped như DbContext (lỗi captive dependency).

### Factory Method (biến thể static factory)

Ý tưởng: tạo đối tượng qua một phương thức có tên, thay vì gọi constructor trực tiếp, để kiểm tra điều kiện trước khi đối tượng tồn tại.

Trong OrderFlow: Order.Create(customerId, items) ở OrderFlow.Domain/Entities/Order.cs, cùng với OrderItem.Create và Customer.Create. Một Order luôn được tạo ở trạng thái hợp lệ, vì không còn cách nào khác để có Order.

Trong URL Shortener: lambda đăng ký ILinkCache trong InfrastructureSetup là một factory. Nó đọc cấu hình rồi quyết định tạo RedisLinkCache, InMemoryLinkCache hay NullLinkCache; phần còn lại của code không biết nó nhận được loại nào.

## Structural: ghép đối tượng

### Decorator

Ý tưởng: bọc một đối tượng bằng một đối tượng khác cùng interface để thêm hành vi, không sửa lớp gốc.

- URL Shortener: CachedRedirectReader bọc SqlRedirectReader (src/UrlShortener.Infrastructure/Links/RedirectReaders.cs). Cả hai đều là IRedirectReader. Decorator thêm cache-aside: đọc cache trước, miss thì gọi lớp bên trong rồi ghi vào cache. Endpoint redirect chỉ biết IRedirectReader. Khi tắt cache (Cache:Provider=None), DI trả thẳng SqlRedirectReader, không có lớp bọc nào, nên chế độ đo "không cache" không tốn thêm bước.
- OrderFlow: ValidationCommandHandlerDecorator<TCommand, TResult> ở OrderFlow.Application/Behaviors/. Nó bọc mọi ICommandHandler, chạy hết validator trước, rồi mới gọi handler thật. Handler không phải tự validate.

### Proxy

Proxy cũng là lớp đứng trước đối tượng thật với cùng interface, nhưng mục đích là kiểm soát việc truy cập (cache, lazy loading, phân quyền) chứ không phải thêm tính năng. CachedRedirectReader có thể gọi là caching proxy, và trong phỏng vấn nói cả hai tên đều được chấp nhận. Em gọi nó là Decorator vì nó được ghép vào qua DI giống hệt ValidationCommandHandlerDecorator.

### Facade

Ý tưởng: một lớp đơn giản che một hệ con phức tạp. LinkService là facade cho phần quản lý link. Endpoint chỉ gọi CreateAsync hay DisableAsync; nó không biết bên trong có retry khi trùng code, có SaveChanges, có xoá cache sau commit.

### Adapter

Ý tưởng: chuyển một interface có sẵn thành interface mà code của mình cần. RedisLinkCache là adapter từ API của StackExchange.Redis (StringGetAsync, RedisValue, RedisException) sang ILinkCache của project. Nó cũng đổi lỗi Redis thành "cache miss" để phần còn lại không phải biết Redis là gì.

## Behavioral: cách các đối tượng phối hợp

### Strategy

Ý tưởng: một họ thuật toán cùng interface, chọn cái nào lúc chạy. ILinkCache có ba strategy là Redis, Memory và None; Cache:Provider quyết định dùng cái nào. Muốn đổi cách cache thì sửa cấu hình, không sửa code redirect. IShortCodeGenerator cũng là strategy: production dùng RandomBase62CodeGenerator, test dùng FakeCodeGenerator để ép trùng code.

### Chain of Responsibility

Ý tưởng: request đi qua một chuỗi handler, mỗi handler xử lý phần của mình rồi chuyển tiếp hoặc dừng lại. Pipeline middleware của ASP.NET Core chính là pattern này. Trong OrderFlow, CorrelationIdMiddleware (OrderFlow.API/Middleware/) gắn correlation id vào request rồi gọi next. Trong URL Shortener, UseExceptionHandler, UseStatusCodePages, UseAuthentication, UseAuthorization là các mắt xích. Thứ tự rất quan trọng: authentication phải đứng trước authorization.

### Command

Ý tưởng: đóng gói một yêu cầu thành một đối tượng dữ liệu, tách khỏi phần thực thi. OrderFlow có ICommand<TResult> và ICommandHandler<TCommand, TResult> (OrderFlow.Application/Abstractions/Messaging/ICommand.cs): command là dữ liệu thuần, handler chứa hành vi. Nhờ tách như vậy mới bọc được decorator validation ở giữa.

### Template Method

Ý tưởng: lớp cha cố định khung thuật toán, lớp con chỉ điền vài bước. ApiKeyAuthenticationHandler kế thừa AuthenticationHandler<TOptions> của ASP.NET Core và chỉ override HandleAuthenticateAsync. Phần đọc scheme, gọi challenge và trả 401 đã do lớp cha làm.

### Observer

Ý tưởng: một đối tượng phát sự kiện, nhiều đối tượng đăng ký nhận. Hiện project chưa dùng ở mức code của mình. Chỗ hợp lý sau này là click analytics (US-11): redirect phát sự kiện "đã click", một worker nghe và ghi thống kê, để redirect không phải chờ ghi DB. Em ghi ra đây là chỗ sẽ dùng, không nhận là đã làm.

## Câu tự trả lời

Kể 3 nhóm design pattern, mỗi nhóm một ví dụ em đã dùng: Creational là Singleton (IConnectionMultiplexer, cache). Structural là Decorator (CachedRedirectReader, ValidationCommandHandlerDecorator). Behavioral là Strategy (ILinkCache chọn theo cấu hình) hoặc Chain of Responsibility (middleware).

Vì sao cache hay là Singleton: cache chỉ có ích khi mọi request dùng chung một kho dữ liệu. Tạo cache theo request hay theo scope thì lúc nào cũng rỗng.
