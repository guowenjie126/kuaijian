using KuaiJian.WinUI;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

var assertionsPassed = 0;
void Assert(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + name);
    assertionsPassed++;
    Console.WriteLine("PASS: " + name);
}

var exact = new QaEntry { Question = "退款流程？", Answer = "精确答案", Aliases = "退款" };
Assert(SettingsSectionPolicy.NavigationSections.SequenceEqual(new[] { "账号", "保存", "下载", "隐藏", "更新" }),
    "设置导航严格遵循规格五个分区及其顺序");
Assert(CalculatorButtonLayoutPolicy.ButtonRows.Count == 5
    && CalculatorButtonLayoutPolicy.ButtonRows.All(row => row.Count == 4)
    && CalculatorButtonLayoutPolicy.ButtonRows.SelectMany(row => row).SequenceEqual(new[]
        { "AC", "⌫", "%", "÷", "7", "8", "9", "×", "4", "5", "6", "−", "1", "2", "3", "+", "±", "0", ".", "=" }),
    "计算器按钮严格遵循规格五行四列顺序");
Assert(CalculatorKeyboardPolicy.TokenFor(0x39, true) == "("
    && CalculatorKeyboardPolicy.TokenFor(0x30, true) == ")"
    && CalculatorKeyboardPolicy.TokenFor(0x35, true) == "%"
    && CalculatorKeyboardPolicy.TokenFor(0x38, true) == "×",
    "计算器 Shift+9/Shift+0 括号、百分比与乘号快捷键映射正确");
Assert(CalculatorKeyboardPolicy.TokenFor(0x35, false) == "5"
    && CalculatorKeyboardPolicy.TokenFor(0x65, false) == "5"
    && CalculatorKeyboardPolicy.TokenFor(0x6B, false) == "+"
    && CalculatorKeyboardPolicy.TokenFor(0x6E, false) == "."
    && CalculatorKeyboardPolicy.TokenFor(0x0D, false) == "="
    && CalculatorKeyboardPolicy.TokenFor(0x08, false) == "⌫"
    && CalculatorKeyboardPolicy.TokenFor(0x41, false) is null,
    "计算器主键盘/数字小键盘输入映射和无关按键过滤正确");
var landscapeFit = ImagePreviewScalePolicy.FitToViewport(1200, 600, 600, 500);
var portraitFit = ImagePreviewScalePolicy.FitToViewport(600, 1200, 600, 500);
var smallFit = ImagePreviewScalePolicy.FitToViewport(100, 50, 600, 500);
Assert(landscapeFit == (600d, 300d)
    && portraitFit == (250d, 500d)
    && smallFit == (100d, 50d)
    && ImagePreviewScalePolicy.FitToViewport(0, 100, 600, 500) == (0d, 0d),
    "图片预览初始尺寸按视口等比适配、不放大小图并拒绝无效尺寸");
Assert(DownloadBrowserPolicy.CookieArguments("none").Count == 0
    && DownloadBrowserPolicy.CookieArguments("unknown").Count == 0,
    "未选择或无效浏览器时不向 yt-dlp 传递 Cookie 参数");
Assert(DownloadBrowserPolicy.CookieArguments(" Chrome ").SequenceEqual(new[] { "--cookies-from-browser", "chrome" })
    && DownloadBrowserPolicy.CookieArguments("Edge").SequenceEqual(new[] { "--cookies-from-browser", "edge" })
    && DownloadBrowserPolicy.CookieArguments("Firefox").SequenceEqual(new[] { "--cookies-from-browser", "firefox" }),
    "仅显式选择受支持浏览器后才生成对应的 yt-dlp Cookie 参数");
Assert(TransferChunkPolicy.GetChunkCount(0) == 0
    && TransferChunkPolicy.GetChunkCount(TransferChunkPolicy.ChunkSize) == 1
    && TransferChunkPolicy.GetChunkCount(TransferChunkPolicy.ChunkSize + 1) == 2
    && TransferChunkPolicy.GetChunkLength(TransferChunkPolicy.ChunkSize + 17, 0) == TransferChunkPolicy.ChunkSize
    && TransferChunkPolicy.GetChunkLength(TransferChunkPolicy.ChunkSize + 17, 1) == 17
    && TransferChunkPolicy.GetChunkOffset(1) == TransferChunkPolicy.ChunkSize
    && TransferChunkPolicy.IsValidSha256(new string('a', 64))
    && !TransferChunkPolicy.IsValidSha256(new string('g', 64)),
    "互传采用 8 MiB 分块，正确计算边界、空文件和 SHA-256 格式");
var transferSendRootA = TransferDataPathPolicy.SendCheckpointRoot(Path.GetTempPath(), "account-a");
var transferSendRootB = TransferDataPathPolicy.SendCheckpointRoot(Path.GetTempPath(), "account-b");
var transferReceiveRootA = TransferDataPathPolicy.ReceiveStagingRoot(Path.GetTempPath(), "account-a");
Assert(transferSendRootA != transferSendRootB
    && transferSendRootA.StartsWith(UserDataPathPolicy.GetUserRoot(Path.GetTempPath(), "account-a"), StringComparison.OrdinalIgnoreCase)
    && transferReceiveRootA.StartsWith(UserDataPathPolicy.GetUserRoot(Path.GetTempPath(), "account-a"), StringComparison.OrdinalIgnoreCase)
    && transferSendRootA != transferReceiveRootA,
    "互传发送检查点与接收暂存按账号隔离并分开存储");
var alias = new QaEntry { Question = "如何退货", Answer = "别名答案", Aliases = "退款|退货流程" };
var keywords = new QaEntry { Question = "商品破损如何处理", Answer = "关键词答案", Keywords = "破损|照片|补发" };
var fuzzy = new QaEntry { Question = "请问什么时候发货", Answer = "模糊候选答案" };
var disabled = new QaEntry { Question = "停用问答", Answer = "不应命中", Enabled = false };
var entries = new[] { alias, keywords, fuzzy, exact, disabled };

Assert(QaMatcher.Find(" 退款流程？ ", entries)?.Entry.Id == exact.Id, "精确问题优先并忽略空格");
Assert(QaMatcher.Find("退货流程", entries)?.Kind == QaMatchKind.Alias, "别名精确匹配");
var keywordHit = QaMatcher.Find("商品破损", entries);
Assert(keywordHit?.Kind == QaMatchKind.Keywords && keywordHit.IsSafeForAutomaticCopy,
    "任一关键词命中即按关键词匹配并允许复制答案");
var partialKeywords = QaMatcher.Find("商品", entries);
Assert(partialKeywords?.Kind == QaMatchKind.Candidate && !partialKeywords.IsSafeForAutomaticCopy,
    "没有关键词命中时的模糊候选不会自动复制答案");
var candidate = QaMatcher.Find("什么时候发货", entries);
Assert(candidate?.Kind == QaMatchKind.Candidate && candidate.IsSafeForAutomaticCopy == false, "模糊匹配只返回不可自动复制的候选");
Assert(QaMatcher.Find("停用问答", entries) is null, "停用问答不参与匹配");
Assert(QaSourceFilter.IsAllowed("微信|Chrome", "chrome"), "来源限制忽略大小写并精确匹配");
Assert(!QaSourceFilter.IsAllowed("微信|Chrome", "Google Chrome"), "来源限制不做模糊包含匹配");
Assert(QaSourceFilter.IsAllowed("", "任意来源"), "空来源限制表示允许所有应用");
var searchableQa = new QaEntry
{
    Question = "退货政策",
    Answer = "请联系客服处理",
    Aliases = "退款|退货申请",
    Keywords = "退货|售后"
};
Assert(QaEntrySearchPolicy.Matches(searchableQa, "政策")
    && QaEntrySearchPolicy.Matches(searchableQa, "联系")
    && QaEntrySearchPolicy.Matches(searchableQa, "退款")
    && QaEntrySearchPolicy.Matches(searchableQa, "售后")
    && QaEntrySearchPolicy.Matches(searchableQa, "  ")
    && !QaEntrySearchPolicy.Matches(searchableQa, "不存在"),
    "问答列表搜索问题、答案、别名和关键词且忽略大小写与首尾空格");
var qaCsvExpected = new QaEntry
{
    Question = "问题,第一行\n第二行",
    Answer = "回答含 \"引号\"",
    Aliases = "别名|备用",
    Keywords = "关键词",
    Category = "售后",
    Note = "备注",
    Enabled = false
};
var qaCsvRows = QaCsvCodec.ParseRows(QaCsvCodec.Serialize(new[] { qaCsvExpected }));
Assert(qaCsvRows.Count == 2 && qaCsvRows[0].SequenceEqual(new[] { "问题", "答案", "别名", "关键词", "分类", "备注", "是否启用" })
    && qaCsvRows[1].SequenceEqual(new[] { qaCsvExpected.Question, qaCsvExpected.Answer, qaCsvExpected.Aliases,
        qaCsvExpected.Keywords, qaCsvExpected.Category, qaCsvExpected.Note, "否" }),
    "问答 CSV 往返保留逗号、双引号、多行文本和停用状态");
var malformedCsvRejected = false;
try { QaCsvCodec.ParseRows("问题,答案\n\"未闭合,回答"); }
catch (FormatException) { malformedCsvRejected = true; }
Assert(malformedCsvRejected, "问答 CSV 拒绝未闭合引号而不静默错列");
var qaImportTarget = new List<QaEntry>
{
    new() { Id = "existing", Question = "旧问题", Answer = "旧答案" }
};
var qaImportedRows = new[]
{
    new QaEntry { Id = "existing", Question = "更新问题", Answer = "更新答案" },
    new QaEntry { Id = "new-entry", Question = "新问题", Answer = "新答案" },
    new QaEntry { Id = "another-entry", Question = "另一个新问题", Answer = "答案" },
    new QaEntry { Id = "new-entry", Question = "重复导入问题", Answer = "最后一个答案" },
    new QaEntry { Id = "empty", Question = "  ", Answer = "" }
};
var qaImportedCount = QaImportMergePolicy.MergeInto(qaImportTarget, qaImportedRows);
Assert(qaImportedCount == 4 && qaImportTarget.Count == 3
    && qaImportTarget[0].Id == "another-entry" && qaImportTarget[1].Id == "new-entry"
    && qaImportTarget[1].Answer == "最后一个答案"
    && qaImportTarget.Single(entry => entry.Id == "existing").Answer == "更新答案",
    "问答批量导入按 ID 更新并插入、后值覆盖、保留置顶顺序并跳过空记录");

var compressedExpansion = new byte[4096];
using var compressedBuffer = new MemoryStream();
await using (var compressor = new GZipStream(compressedBuffer, CompressionLevel.SmallestSize, leaveOpen: true))
    await compressor.WriteAsync(compressedExpansion);
compressedBuffer.Position = 0;
var compressedExpansionRejected = false;
try { await BoundedStreamReader.ReadBytesAsync(new GZipStream(compressedBuffer, CompressionMode.Decompress), 1024); }
catch (InvalidDataException) { compressedExpansionRejected = true; }
Assert(compressedExpansionRejected, "压缩历史文件解压时按展开后大小限制并提前拒绝超限内容");
var boundedPayload = Enumerable.Range(0, 1024).Select(value => (byte)value).ToArray();
var boundedRead = await BoundedStreamReader.ReadBytesAsync(new MemoryStream(boundedPayload), boundedPayload.Length);
Assert(boundedRead.SequenceEqual(boundedPayload), "有界历史文件读取允许正好达到上限的内容");
using (var cancelledRead = new CancellationTokenSource())
{
    cancelledRead.Cancel();
    var readCancelled = false;
    try { await BoundedStreamReader.ReadBytesAsync(new MemoryStream(boundedPayload), boundedPayload.Length, cancelledRead.Token); }
    catch (OperationCanceledException) { readCancelled = true; }
    Assert(readCancelled, "有界历史文件读取遵守取消令牌");
}

Assert(CalculatorEngine.Evaluate("2+3×4") == 14, "计算器遵循乘除优先级");
Assert(CalculatorEngine.Evaluate("(2+3)×4") == 20, "计算器支持括号分组");
Assert(CalculatorEngine.Evaluate("−(8−10)/2") == 1, "计算器支持括号前的一元负号");
Assert(CalculatorEngine.Evaluate("50%+1") == 1.5, "计算器支持百分数后缀");
var malformedExpressionRejected = false;
try { CalculatorEngine.Evaluate("(1+2"); }
catch (FormatException) { malformedExpressionRejected = true; }
Assert(malformedExpressionRejected, "计算器拒绝不匹配括号而不崩溃");
Assert(CalculatorEngine.Evaluate("4÷0") == 0, "计算器除数为零时显示零");
Assert(CalculatorEngine.Evaluate("4/(2−2)") == 0, "括号计算得到零除数时显示零");
Assert(ClipboardUrlPolicy.TryCreateWebUri(" https://example.com/path ", out var secureWebUri)
    && secureWebUri?.Scheme == Uri.UriSchemeHttps, "网址打开策略接受 HTTPS 并清理首尾空格");
Assert(ClipboardUrlPolicy.TryCreateWebUri("http://example.com", out var plainWebUri)
    && plainWebUri?.Scheme == Uri.UriSchemeHttp, "网址打开策略接受 HTTP");
Assert(!ClipboardUrlPolicy.TryCreateWebUri("javascript:alert(1)", out _)
    && !ClipboardUrlPolicy.TryCreateWebUri("file:///C:/Windows/win.ini", out _)
    && !ClipboardUrlPolicy.TryCreateWebUri("example.com", out _), "网址打开策略拒绝脚本、文件和相对 URI");
Assert(ClipboardCapturePolicy.ShouldCaptureText(false, true, false)
    && !ClipboardCapturePolicy.ShouldCaptureText(false, false, true), "保存文本开关只影响非网址文本");
Assert(ClipboardCapturePolicy.ShouldCaptureText(true, true, true)
    && !ClipboardCapturePolicy.ShouldCaptureText(true, true, false), "保存网址开关独立控制网址捕获");
Assert(ClipboardCapturePolicy.ShouldCaptureImage(true) && !ClipboardCapturePolicy.ShouldCaptureImage(false),
    "保存图片开关控制图片捕获");
Assert(ClipboardCapturePolicy.ShouldCaptureFilePaths(true) && !ClipboardCapturePolicy.ShouldCaptureFilePaths(false),
    "保存文本开关控制文件路径捕获");
Assert(ClipboardCapturePolicy.IsSnapshotCurrent(42, 42)
    && !ClipboardCapturePolicy.IsSnapshotCurrent(42, 43),
    "剪贴板来源与内容读取期间序列号变化时拒绝不一致快照");
var orphanCapture = Path.Combine(Path.GetTempPath(), "KuaiJian-orphan-capture-" + Guid.NewGuid().ToString("N"));
await File.WriteAllTextAsync(orphanCapture, "partial capture");
ClipboardCaptureCleanup.DeleteOrphanedFile(orphanCapture);
Assert(!File.Exists(orphanCapture), "剪贴板异步捕获中断后清理未移交的临时文件");
ClipboardCaptureCleanup.DeleteOrphanedFile(null);
var corruptJsonDirectory = Path.Combine(Path.GetTempPath(), "KuaiJian-corrupt-json-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(corruptJsonDirectory);
var corruptJsonPath = Path.Combine(corruptJsonDirectory, "clipboard.json");
const string corruptJsonContent = "[{ malformed";
await File.WriteAllTextAsync(corruptJsonPath, corruptJsonContent);
var corruptJsonWarnings = new List<string>();
try
{
    var recoveredCorruptJson = await UserDataJsonStore.LoadPreservingCorruptAsync<List<string>>(
        corruptJsonPath, new JsonSerializerOptions(), corruptJsonWarnings.Add);
    var corruptBackups = Directory.GetFiles(corruptJsonDirectory, "clipboard.json.corrupt-*");
    Assert(recoveredCorruptJson is null && File.ReadAllText(corruptJsonPath) == corruptJsonContent
        && corruptBackups.Length == 1 && File.ReadAllText(corruptBackups[0]) == corruptJsonContent
        && corruptJsonWarnings.Count == 1 && corruptJsonWarnings[0].Contains("已备份", StringComparison.Ordinal),
        "损坏的用户 JSON 保留原文件、创建完整备份并报告恢复提示");
}
finally
{
    Directory.Delete(corruptJsonDirectory, true);
}
var atomicJsonDirectory = Path.Combine(Path.GetTempPath(), "KuaiJian-atomic-json-" + Guid.NewGuid().ToString("N"));
var atomicJsonPath = Path.Combine(atomicJsonDirectory, "clipboard.json");
var atomicJsonOptions = new JsonSerializerOptions();
try
{
    var writes = Enumerable.Range(0, 12)
        .Select(value => UserDataJsonStore.SaveAtomicallyAsync(
            atomicJsonPath, Enumerable.Repeat(value, 1024).ToArray(), atomicJsonOptions));
    await Task.WhenAll(writes);
    var finalJson = JsonSerializer.Deserialize<int[]>(await File.ReadAllTextAsync(atomicJsonPath));
    var concurrentWriteComplete = finalJson is { Length: 1024 }
        && finalJson.All(value => value == finalJson[0])
        && finalJson[0] is >= 0 and < 12
        && !File.Exists(atomicJsonPath + ".tmp");
    var originalJson = await File.ReadAllTextAsync(atomicJsonPath);
    var cyclicValue = new List<object>();
    cyclicValue.Add(cyclicValue);
    var serializationFailed = false;
    try { await UserDataJsonStore.SaveAtomicallyAsync(atomicJsonPath, cyclicValue, atomicJsonOptions); }
    catch (JsonException) { serializationFailed = true; }
    Assert(concurrentWriteComplete && serializationFailed
        && await File.ReadAllTextAsync(atomicJsonPath) == originalJson
        && !File.Exists(atomicJsonPath + ".tmp"),
        "并发 JSON 保存保持完整且序列化失败时保留旧文件并清理临时文件");
}
finally
{
    if (Directory.Exists(atomicJsonDirectory)) Directory.Delete(atomicJsonDirectory, true);
}
var accountIsolationRoot = Path.Combine(Path.GetTempPath(), "KuaiJian-account-isolation-" + Guid.NewGuid().ToString("N"));
try
{
    var firstAccountRoot = UserDataPathPolicy.GetUserRoot(accountIsolationRoot, "account-alpha");
    var secondAccountRoot = UserDataPathPolicy.GetUserRoot(accountIsolationRoot, "account-beta");
    var traversalRejected = false;
    try { UserDataPathPolicy.GetUserRoot(accountIsolationRoot, "..\\outside"); }
    catch (InvalidDataException) { traversalRejected = true; }
    var firstAccountFile = Path.Combine(firstAccountRoot, "clipboard-items.json");
    var secondAccountFile = Path.Combine(secondAccountRoot, "clipboard-items.json");
    await UserDataJsonStore.SaveAtomicallyAsync(firstAccountFile, new[] { "alpha-private" }, atomicJsonOptions);
    await UserDataJsonStore.SaveAtomicallyAsync(secondAccountFile, new[] { "beta-private" }, atomicJsonOptions);
    var firstAccountData = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(firstAccountFile));
    var secondAccountData = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(secondAccountFile));
    Assert(!string.Equals(firstAccountRoot, secondAccountRoot, StringComparison.OrdinalIgnoreCase)
        && firstAccountData is ["alpha-private"] && secondAccountData is ["beta-private"]
        && traversalRejected,
        "不同账号数据分别持久化到隔离目录且路径穿越账号 ID 被拒绝");
}
finally
{
    if (Directory.Exists(accountIsolationRoot)) Directory.Delete(accountIsolationRoot, true);
}
var copyQParserCalls = 0;
var csvJsonAttempt = await ClipboardImportFormatPolicy.TryParseCopyQJsonAsync("text,title\nhello,Example", _ =>
{
    copyQParserCalls++;
    return Task.FromResult<string?>("parsed");
});
var malformedJsonAttempt = await ClipboardImportFormatPolicy.TryParseCopyQJsonAsync("{ malformed", _ =>
    Task.FromException<string?>(new JsonException("Malformed JSON")));
var copyQJsonAttempt = await ClipboardImportFormatPolicy.TryParseCopyQJsonAsync("  {\"copyq_items\": []}", _ =>
    Task.FromResult<string?>("copyq"));
Assert(csvJsonAttempt is null && copyQParserCalls == 0 && malformedJsonAttempt is null
    && copyQJsonAttempt == "copyq",
    "CSV 不误入 CopyQ JSON 解析，畸形 JSON 可回退且有效 CopyQ JSON 正常解析");
var legacyClipboardCsv = ClipboardHistoryCsvCodec.Parse(
    "text,title,source,note\r\n\"第一行,带逗号\n第二行\",\"标题 \"\"带引号\"\"\",Chrome,备注\r\nhttps://example.com,网址,浏览器,链接");
Assert(legacyClipboardCsv.Count == 2
    && legacyClipboardCsv[0].Text == "第一行,带逗号\n第二行"
    && legacyClipboardCsv[0].Title == "标题 \"带引号\""
    && legacyClipboardCsv[0].Source == "Chrome" && legacyClipboardCsv[0].Note == "备注"
    && !legacyClipboardCsv[0].IsUrl
    && legacyClipboardCsv[1].IsUrl && legacyClipboardCsv[1].Title == "网址",
    "旧版剪贴板 CSV 真实解析保留逗号、多行、转义引号、来源备注并识别网址");
var legacyHeaderlessCsv = ClipboardHistoryCsvCodec.Parse("无标题纯文本,其他列\r\n第二条文本");
Assert(legacyHeaderlessCsv.Count == 2 && legacyHeaderlessCsv[0].Title == "无标题纯文本"
    && legacyHeaderlessCsv[0].Source == "旧版历史",
    "旧版无表头 CSV 以首列作为内容并补齐默认来源");
var malformedHistoryCsvRejected = false;
try { ClipboardHistoryCsvCodec.Parse("text,title\n\"未闭合,标题"); }
catch (FormatException) { malformedHistoryCsvRejected = true; }
Assert(malformedHistoryCsvRejected, "旧版剪贴板 CSV 拒绝未闭合引号");
var legacyMacCrCsv = ClipboardHistoryCsvCodec.Parse("text,title\r第一条,标题一\r第二条,标题二");
Assert(legacyMacCrCsv.Count == 2 && legacyMacCrCsv[0].Text == "第一条"
    && legacyMacCrCsv[1].Title == "标题二",
    "旧版剪贴板 CSV 兼容单独 CR 记录分隔符");
var unescapedQuoteCsvRejected = false;
try { ClipboardHistoryCsvCodec.Parse("text,title\n未转义\"引号,标题"); }
catch (FormatException) { unescapedQuoteCsvRejected = true; }
var trailingQuoteJunkCsvRejected = false;
try { ClipboardHistoryCsvCodec.Parse("text,title\n\"已闭合\"后缀,标题"); }
catch (FormatException) { trailingQuoteJunkCsvRejected = true; }
Assert(unescapedQuoteCsvRejected && trailingQuoteJunkCsvRejected,
    "旧版剪贴板 CSV 拒绝未转义引号及闭引号后的杂字符");
var relativeNow = new DateTimeOffset(new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Local));
Assert(RelativeTimePolicy.Format(relativeNow.AddSeconds(-4), relativeNow) == "4 秒前"
    && RelativeTimePolicy.Format(relativeNow.AddMinutes(-5), relativeNow) == "5 分钟前"
    && RelativeTimePolicy.Format(relativeNow.AddHours(-2), relativeNow) == "2 小时前"
    && RelativeTimePolicy.Format(relativeNow.AddDays(-1), relativeNow) == "昨天"
    && RelativeTimePolicy.Format(relativeNow.AddDays(-2), relativeNow) == "10月19日",
    "剪贴板记录时间按规格显示秒、分钟、小时、昨天及更早的日历日期");
var importBudget = new LegacyClipboardImportBudget();
importBudget.ReserveInput(LegacyClipboardImportBudget.MaximumBytes);
var inputBudgetRejected = false;
try { importBudget.ReserveInput(1); } catch (InvalidDataException) { inputBudgetRejected = true; }
var expandedBudget = new LegacyClipboardImportBudget();
expandedBudget.ReserveExpanded(LegacyClipboardImportBudget.MaximumBytes);
var expandedBudgetRejected = false;
try { expandedBudget.ReserveExpanded(1); } catch (InvalidDataException) { expandedBudgetRejected = true; }
Assert(inputBudgetRejected && expandedBudgetRejected,
    "多文件剪贴板导入分别限制累计源文件量和解压后总量");
var imageIdentityRoot = Path.Combine(Path.GetTempPath(), "KuaiJian-image-identity-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(imageIdentityRoot);
try
{
    var imageBytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4 };
    var sameImageA = Path.Combine(imageIdentityRoot, "a.png");
    var sameImageB = Path.Combine(imageIdentityRoot, "b.png");
    var differentImage = Path.Combine(imageIdentityRoot, "different.png");
    await File.WriteAllBytesAsync(sameImageA, imageBytes);
    await File.WriteAllBytesAsync(sameImageB, imageBytes);
    await File.WriteAllBytesAsync(differentImage, imageBytes.Concat(new byte[] { 5 }).ToArray());
    var imageKeyA = await ClipboardContentIdentity.CreateKeyAsync(true, "Image", "", null, sameImageA, null, Array.Empty<string>());
    var imageKeyB = await ClipboardContentIdentity.CreateKeyAsync(true, "Image", "", null, sameImageB, null, Array.Empty<string>());
    var imageKeyDifferent = await ClipboardContentIdentity.CreateKeyAsync(true, "Image", "", null, differentImage, null, Array.Empty<string>());
    Assert(imageKeyA == imageKeyB, "相同图片内容在不同临时路径下生成相同剪贴板身份");
    Assert(imageKeyA != imageKeyDifferent, "不同图片内容生成不同剪贴板身份");
    var existingImageImport = (Kind: "Image", Text: "", Html: (string?)null, ImagePath: sameImageA,
        RtfPath: (string?)null, FileUrls: (IEnumerable<string>)Array.Empty<string>());
    var duplicateImageImport = (Kind: "Image", Text: "", Html: (string?)null, ImagePath: sameImageB,
        RtfPath: (string?)null, FileUrls: (IEnumerable<string>)Array.Empty<string>());
    var distinctImageImport = (Kind: "Image", Text: "", Html: (string?)null, ImagePath: differentImage,
        RtfPath: (string?)null, FileUrls: (IEnumerable<string>)Array.Empty<string>());
    var imageMerge = await ClipboardImportMergePolicy.SelectUniqueAsync(
        new[] { existingImageImport }, new[] { duplicateImageImport, distinctImageImport }, _ => true,
        item => (item.Kind, item.Text, item.Html, item.Kind == "Text" && !string.IsNullOrWhiteSpace(item.RtfPath)),
        item => ClipboardContentIdentity.CreateKeyAsync(item.Kind == "Image", item.Kind, item.Text, item.Html,
            item.ImagePath, item.RtfPath, item.FileUrls));
    Assert(imageMerge.Unique.Count == 1 && imageMerge.Unique[0].ImagePath == differentImage
        && imageMerge.Skipped.Count == 1 && imageMerge.Skipped[0].ImagePath == sameImageB,
        "导入图片按内容而非新生成的临时路径跳过重复记录");

    var sameRtfA = Path.Combine(imageIdentityRoot, "a.rtf");
    var sameRtfB = Path.Combine(imageIdentityRoot, "b.rtf");
    var differentRtf = Path.Combine(imageIdentityRoot, "different.rtf");
    await File.WriteAllTextAsync(sameRtfA, "{\\rtf1\\b Same}");
    await File.WriteAllTextAsync(sameRtfB, "{\\rtf1\\b Same}");
    await File.WriteAllTextAsync(differentRtf, "{\\rtf1 Same}");
    var rtfKeyA = await ClipboardContentIdentity.CreateKeyAsync(false, "Text", "Same", null, null, sameRtfA, Array.Empty<string>());
    var rtfKeyB = await ClipboardContentIdentity.CreateKeyAsync(false, "Text", "Same", null, null, sameRtfB, Array.Empty<string>());
    var rtfKeyDifferent = await ClipboardContentIdentity.CreateKeyAsync(false, "Text", "Same", null, null, differentRtf, Array.Empty<string>());
    Assert(rtfKeyA == rtfKeyB, "相同 RTF 格式内容在不同临时路径下生成相同剪贴板身份");
    Assert(rtfKeyA != rtfKeyDifferent, "相同纯文本的不同 RTF 格式保留为不同剪贴板身份");
    var existingRtfImport = (Kind: "Text", Text: "Same", Html: (string?)null, ImagePath: (string?)null,
        RtfPath: sameRtfA, FileUrls: (IEnumerable<string>)Array.Empty<string>());
    var duplicateRtfImport = (Kind: "Text", Text: "Same", Html: (string?)null, ImagePath: (string?)null,
        RtfPath: sameRtfB, FileUrls: (IEnumerable<string>)Array.Empty<string>());
    var distinctRtfImport = (Kind: "Text", Text: "Same", Html: (string?)null, ImagePath: (string?)null,
        RtfPath: differentRtf, FileUrls: (IEnumerable<string>)Array.Empty<string>());
    var rtfMerge = await ClipboardImportMergePolicy.SelectUniqueAsync(
        new[] { existingRtfImport }, new[] { duplicateRtfImport, distinctRtfImport }, _ => true,
        item => (item.Kind, item.Text, item.Html, item.Kind == "Text" && !string.IsNullOrWhiteSpace(item.RtfPath)),
        item => ClipboardContentIdentity.CreateKeyAsync(false, item.Kind, item.Text, item.Html,
            item.ImagePath, item.RtfPath, item.FileUrls));
    Assert(rtfMerge.Unique.Count == 1 && rtfMerge.Unique[0].RtfPath == differentRtf
        && rtfMerge.Skipped.Count == 1 && rtfMerge.Skipped[0].RtfPath == sameRtfB,
        "导入 RTF 按内容跳过重复记录并保留不同格式");
}
finally
{
    if (Directory.Exists(imageIdentityRoot)) Directory.Delete(imageIdentityRoot, recursive: true);
}
Assert(DownloadCompletionPolicy.Normalize(null) == DownloadCompletionPolicy.None
    && !DownloadCompletionPolicy.ShouldRevealInFolder("unknown"),
    "未知下载完成设置安全回退为不执行操作");
Assert(DownloadCompletionPolicy.ShouldRevealInFolder("REVEALINFOLDER"),
    "下载完成行为可按用户设置定位文件但不自动打开");
var resumeMetadata = new DownloadResumeMetadata("file.bin", "\"v1\"", null, 10);
Assert(DownloadResumePolicy.HasStableValidator(resumeMetadata)
    && !DownloadResumePolicy.HasStableValidator(new DownloadResumeMetadata("file.bin", "W/\"weak\"", null, 10))
    && !DownloadResumePolicy.HasStableValidator(new DownloadResumeMetadata("file.bin", null, null, 10)),
    "断点续传仅接受带稳定实体校验器的部分文件");
Assert(DownloadResumePolicy.CanAppendRange(4, HttpStatusCode.PartialContent, 4,
        "\"v1\"", null, "\"v1\"", null)
    && !DownloadResumePolicy.CanAppendRange(4, HttpStatusCode.PartialContent, 3,
        "\"v1\"", null, "\"v1\"", null)
    && !DownloadResumePolicy.CanAppendRange(4, HttpStatusCode.PartialContent, 4,
        "\"v1\"", null, "\"v2\"", null),
    "续传仅追加起点和实体标签都匹配的 HTTP 206 响应");
var ytResumeRoot = Path.Combine(Path.GetTempPath(), "KuaiJian-ytdlp-resume-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(ytResumeRoot);
    Assert(!YtDlpResumePolicy.HasResumableArtifacts(ytResumeRoot),
        "空 yt-dlp staging 目录不显示续传操作");
    await File.WriteAllTextAsync(Path.Combine(ytResumeRoot, "video.f137.part"), "partial-fragment");
    Assert(YtDlpResumePolicy.HasResumableArtifacts(ytResumeRoot),
        "yt-dlp 部分媒体片段存在时任务可继续下载");
}
finally
{
    if (Directory.Exists(ytResumeRoot)) Directory.Delete(ytResumeRoot, recursive: true);
}

var resumeTestRoot = Path.Combine(Path.GetTempPath(), "KuaiJian-http-resume-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(resumeTestRoot);
    var partial = Path.Combine(resumeTestRoot, "payload.part");
    var output = Path.Combine(resumeTestRoot, "output");
    var payload = Encoding.UTF8.GetBytes("0123456789");
    DownloadResumeMetadata? savedMetadata = null;
    using var firstCancellation = new CancellationTokenSource();
    using var firstClient = new HttpClient(new DelegateHttpMessageHandler((request, _) =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload[..5])
        };
        response.Headers.AcceptRanges.Add("bytes");
        response.Headers.ETag = EntityTagHeaderValue.Parse("\"v1\"");
        response.Content.Headers.ContentLength = payload.Length;
        response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "payload.bin" };
        return Task.FromResult(response);
    }));
    var firstCancelled = false;
    try
    {
        await ResumableHttpDownload.DownloadAsync(firstClient, new Uri("https://example.test/payload"), output, partial, null,
            (metadata, _) => { savedMetadata = metadata; return Task.CompletedTask; },
            (_, _) => firstCancellation.Cancel(), firstCancellation.Token);
    }
    catch (OperationCanceledException) { firstCancelled = true; }
    Assert(firstCancelled && savedMetadata is not null && File.Exists(partial) && new FileInfo(partial).Length == 5,
        "HTTP 下载取消后保留带 ETag 的部分文件供续传");

    var rangeObserved = false;
    using var resumeClient = new HttpClient(new DelegateHttpMessageHandler((request, _) =>
    {
        rangeObserved = request.Headers.Range?.Ranges.Single().From == 5
            && request.Headers.IfRange?.EntityTag?.Tag == "\"v1\"";
        var remaining = payload[5..];
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(remaining)
        };
        response.Headers.ETag = EntityTagHeaderValue.Parse("\"v1\"");
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(5, payload.Length - 1, payload.Length);
        response.Content.Headers.ContentLength = remaining.Length;
        return Task.FromResult(response);
    }));
    var resumed = await ResumableHttpDownload.DownloadAsync(resumeClient, new Uri("https://example.test/payload"), output, partial,
        savedMetadata, (_, _) => Task.CompletedTask, (_, _) => { }, CancellationToken.None);
    Assert(rangeObserved && File.ReadAllBytes(resumed.OutputPath).SequenceEqual(payload),
        "HTTP Range/If-Range 续传合并后文件字节与原始内容一致");

    var restartPartial = Path.Combine(resumeTestRoot, "restart.part");
    await File.WriteAllBytesAsync(restartPartial, payload[..5]);
    var fullResponseOnRange = false;
    using var restartClient = new HttpClient(new DelegateHttpMessageHandler((request, _) =>
    {
        fullResponseOnRange = request.Headers.Range is not null;
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
        response.Headers.ETag = EntityTagHeaderValue.Parse("\"v2\"");
        response.Content.Headers.ContentLength = payload.Length;
        response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "replacement.bin" };
        return Task.FromResult(response);
    }));
    var restarted = await ResumableHttpDownload.DownloadAsync(restartClient, new Uri("https://example.test/payload"), output,
        restartPartial, new DownloadResumeMetadata("payload.bin", "\"v1\"", null, payload.Length),
        (_, _) => Task.CompletedTask, (_, _) => { }, CancellationToken.None);
    Assert(fullResponseOnRange && File.ReadAllBytes(restarted.OutputPath).SequenceEqual(payload),
        "服务器忽略 Range 或资源版本变化时清空旧部分并安全完整重下");
}
finally
{
    if (Directory.Exists(resumeTestRoot)) Directory.Delete(resumeTestRoot, recursive: true);
}

var downloadGate = new DownloadConcurrencyGate(1);
using (var firstSlot = await downloadGate.AcquireAsync())
using (var cancelledQueue = new CancellationTokenSource())
{
    var waitingCancellation = downloadGate.AcquireAsync(cancelledQueue.Token);
    cancelledQueue.Cancel();
    var cancellationObserved = false;
    try { await waitingCancellation; }
    catch (OperationCanceledException) { cancellationObserved = true; }
    var queuedSlot = downloadGate.AcquireAsync();
    downloadGate.SetLimit(2);
    using var secondSlot = await queuedSlot;
    Assert(cancellationObserved && downloadGate.PendingCount == 0 && downloadGate.ActiveCount == 2 && downloadGate.Limit == 2,
        "下载并发闸门支持排队取消和运行中调整上限");
    downloadGate.SetLimit(1);
}
Assert(downloadGate.ActiveCount == 0 && downloadGate.Limit == 1,
    "下载并发闸门在动态限流后正确释放全部槽位");
Assert(DownloadTaskOperationPolicy.CanStart("task-a", Array.Empty<string>())
    && !DownloadTaskOperationPolicy.CanStart("task-a", new[] { "task-a" })
    && !DownloadTaskOperationPolicy.CanStart(null, Array.Empty<string>()),
    "同一下载任务已有活动操作时禁止重复启动");

var retainCandidates = new[]
{
    new RetentionRecord("old", DateTimeOffset.UnixEpoch, true),
    new RetentionRecord("pinned-new", DateTimeOffset.UnixEpoch.AddDays(1), true),
    new RetentionRecord("recent", DateTimeOffset.UnixEpoch.AddDays(3), false),
    new RetentionRecord("newest", DateTimeOffset.UnixEpoch.AddDays(4), false)
};
var retained = ClipboardRetentionPolicy.Retain(retainCandidates, 50, item => item.Pinned, item => item.CreatedAt);
Assert(retained.Select(item => item.Id).SequenceEqual(new[] { "pinned-new", "old", "newest", "recent" }),
    "剪贴板保留策略优先保留置顶项，再按时间保留最新记录");
Assert(ClipboardRetentionPolicy.NormalizeLimit(10) == ClipboardRetentionPolicy.MinimumLimit
    && ClipboardRetentionPolicy.NormalizeLimit(5000) == ClipboardRetentionPolicy.MaximumLimit,
    "剪贴板数量上限设置被规范化到支持区间");
using (var activationSignal = new AutoResetEvent(false))
{
    Assert(!WindowsShellService.WaitForActivationSignal(activationSignal, 10), "单实例唤醒等待超时不会反复显示窗口");
    activationSignal.Set();
    Assert(WindowsShellService.WaitForActivationSignal(activationSignal, 500), "收到单实例唤醒信号时显示窗口");
}
var updateHash = new string('A', 64);
Assert(UpdateManifestPolicy.IsValidWindowsX64("windows-x64", "0.4.1", "https://download.example/KuaiJian.exe", updateHash, "修复"),
    "更新清单接受带 HTTPS 与 SHA-256 的 Windows x64 安装包");
Assert(!UpdateManifestPolicy.IsValidWindowsX64("macos-arm64", "0.4.1", "https://download.example/KuaiJian.exe", updateHash, ""),
    "更新清单拒绝其他平台安装包");
Assert(!UpdateManifestPolicy.IsValidWindowsX64(null, "0.2.1", "https://api.kuaijian.jingronghao.com/releases/快剪原生版-0.2.1-macOS.dmg", null, "快剪原生版当前稳定版本"),
    "更新清单拒绝缺少平台和 SHA-256 的旧版 macOS 清单");
Assert(!UpdateManifestPolicy.IsValidWindowsX64("windows-x64", "0.4.1", "http://download.example/KuaiJian.exe", updateHash, ""),
    "更新清单拒绝不安全的 HTTP 下载地址");
Assert(!UpdateManifestPolicy.IsValidWindowsX64("windows-x64", "0.4.1", "https://download.example/KuaiJian.dmg", updateHash, ""),
    "更新清单拒绝 macOS DMG 安装包");
Assert(!UpdateManifestPolicy.IsValidWindowsX64("windows-x64", "0.4.1", "https://download.example/KuaiJian.exe", "", ""),
    "更新清单拒绝缺少 SHA-256 校验值的安装包");
Assert(!UpdateManifestPolicy.IsValidWindowsX64("windows-x64", "not-a-version", "https://download.example/KuaiJian.exe", updateHash, ""),
    "更新清单拒绝无效版本号");
Assert(SupportedSitePolicy.IsSupported(new Uri("https://www.youtube.com/watch?v=example"))
    && SupportedSitePolicy.IsSupported(new Uri("https://m.bilibili.com/video/example")),
    "多站点识别接受官方主域与合法子域");
Assert(!SupportedSitePolicy.IsSupported(new Uri("https://notyoutube.com/video"))
    && !SupportedSitePolicy.IsSupported(new Uri("https://youtube.com.attacker.example/video")),
    "多站点识别拒绝相似或后缀欺骗域名");
Assert(!SupportedSitePolicy.IsSupported(new Uri("ftp://youtube.com/video")),
    "多站点识别仅接受 HTTP 或 HTTPS 协议");
Assert(!SupportedSitePolicy.IsSupported(new Uri("youtube.com/video", UriKind.Relative)),
    "多站点识别拒绝相对 URI 而不抛出异常");

Console.WriteLine("所有问答助手验证通过。");

var localClipboard = new[]
{
    new SyncRecord("local-pending", "本机尚未上传的内容"),
    new SyncRecord("same-id", "本地版本")
};
var remoteClipboard = new[]
{
    new SyncRecord("remote-only", "另一台设备的内容"),
    new SyncRecord("deleted-here", "已在本机删除的远端记录"),
    new SyncRecord("same-id", "云端版本")
};
var visibleRemoteClipboard = ClipboardSyncMerge.ExcludeDeleted(
    remoteClipboard,
    new[] { "deleted-here" },
    record => record.Id);
Assert(visibleRemoteClipboard.All(record => record.Id != "deleted-here"),
    "本机删除标记过滤后续云端轮询结果");
var mergedClipboard = ClipboardSyncMerge.Merge(
    localClipboard,
    visibleRemoteClipboard,
    record => record.Id,
    (remote, local) => new SyncRecord(remote.Id, remote.Text + "+本地资源:" + local.Text));
Assert(mergedClipboard.Select(record => record.Id).Distinct().Count() == 3,
    "云端同步按 ID 去重并同时保留本地与远端新记录");
Assert(mergedClipboard.Single(record => record.Id == "same-id").Text == "云端版本+本地资源:本地版本",
    "同 ID 记录由调用方合并云端字段和本地资源");
var contentMergedClipboard = ClipboardSyncMerge.MergeByContent(
    new[]
    {
        new SyncRecord("windows-id", "跨设备相同文本"),
        new SyncRecord("local-only", "本机新增文本")
    },
    new[]
    {
        new SyncRecord("mac-id", "跨设备相同文本"),
        new SyncRecord("remote-only", "另一设备新增文本")
    },
    record => record.Id,
    record => record.Text,
    (preferred, duplicate) => new SyncRecord(preferred.Id, preferred.Text + "+同ID合并"),
    (preferred, duplicate) => new SyncRecord(preferred.Id, preferred.Text + "+同内容合并"));
Assert(contentMergedClipboard.Count == 3
    && contentMergedClipboard.Count(record => record.Text.StartsWith("跨设备相同文本", StringComparison.Ordinal)) == 1
    && contentMergedClipboard.Any(record => record.Id == "mac-id"),
    "不同设备为相同文本生成不同 ID 时按内容合并并保留云端规范 ID");
var duplicateRemoteMergedClipboard = ClipboardSyncMerge.MergeByContent(
    Array.Empty<SyncRecord>(),
    new[] { new SyncRecord("remote-a", "相同"), new SyncRecord("remote-b", "相同") },
    record => record.Id,
    record => record.Text,
    (preferred, duplicate) => preferred,
    (preferred, duplicate) => preferred);
Assert(duplicateRemoteMergedClipboard.Count == 1,
    "云端快照内部重复内容在合并时去重");
Assert(ClipboardSyncPolicy.IsValidRecord("id", "标题", "内容", null, "Chrome", "备注"),
    "云端记录字段在上限内时接受");
Assert(!ClipboardSyncPolicy.IsValidRecord("", "标题", "内容", null, "Chrome", "备注"),
    "拒绝缺少稳定 ID 的云端记录");
Assert(!ClipboardSyncPolicy.IsValidRecord("id", "标题", new string('字', ClipboardSyncPolicy.MaxTextLength + 1), null, "Chrome", "备注"),
    "拒绝超过字段上限的云端文本");
Assert(ClipboardSyncPolicy.IsPayloadLengthAllowed(null)
    && ClipboardSyncPolicy.IsPayloadLengthAllowed(ClipboardSyncPolicy.MaxPayloadBytes)
    && !ClipboardSyncPolicy.IsPayloadLengthAllowed((long)ClipboardSyncPolicy.MaxPayloadBytes + 1),
    "云端响应体大小限制允许无 Content-Length 的安全流式校验并拒绝超限");
Assert(ClipboardSyncPolicy.IsSyncable(false, false, false, 0), "普通文字和网址允许云同步");
Assert(ClipboardSyncPolicy.ContentKey("https://example.test/a\r\nb", null)
        == ClipboardSyncPolicy.ContentKey("https://example.test/a\nb", null)
    && ClipboardSyncPolicy.ContentKey("同一段文字", "<p>macOS 格式</p>")
        == ClipboardSyncPolicy.ContentKey("同一段文字", "<div>Windows 格式</div>")
    && ClipboardSyncPolicy.ContentKey("", "<b>a\r\nb</b>")
        == ClipboardSyncPolicy.ContentKey(null, "<b>a\nb</b>")
    && ClipboardSyncPolicy.ContentKey("   ", null) is null,
    "跨设备内容身份优先使用可移植文本、统一换行并比较 HTML-only 记录");
Assert(!ClipboardSyncPolicy.IsSyncable(true, false, false, 0)
    && !ClipboardSyncPolicy.IsSyncable(false, true, false, 0)
    && !ClipboardSyncPolicy.IsSyncable(false, false, true, 0)
    && !ClipboardSyncPolicy.IsSyncable(false, false, false, 1),
    "图片、富文本资源和文件路径不会被标记为可同步");
Assert(AuthResponsePolicy.IsSafeAccountId("user-1842")
    && !AuthResponsePolicy.IsSafeAccountId("../outside")
    && !AuthResponsePolicy.IsSafeAccountId("C:\\outside")
    && !AuthResponsePolicy.IsSafeAccountId("CON")
    && !AuthResponsePolicy.IsSafeAccountId("NUL.json")
    && !AuthResponsePolicy.IsSafeAccountId(Guid.Empty.ToString()),
    "认证返回的账号 ID 必须是非空且安全的本地目录名");
Assert(AuthResponsePolicy.IsValidAccessToken("header.payload.signature")
    && !AuthResponsePolicy.IsValidAccessToken("bad token")
    && !AuthResponsePolicy.IsValidAccessToken(new string('x', AuthResponsePolicy.MaxAccessTokenLength + 1)),
    "凭据令牌拒绝空白字符和超长内容");
Assert(AuthResponsePolicy.IsInvalidRestoreSessionResponse(HttpStatusCode.Unauthorized, "server_error")
    && AuthResponsePolicy.IsInvalidRestoreSessionResponse(HttpStatusCode.OK, "token_expired")
    && !AuthResponsePolicy.IsInvalidRestoreSessionResponse(HttpStatusCode.Forbidden, "access_denied"),
    "启动恢复登录根据 HTTP 401 或明确 Token 错误清除失效凭据");
Assert(CloudSyncResponsePolicy.ShouldExpireSession(HttpStatusCode.Unauthorized, "server_error")
    && CloudSyncResponsePolicy.ShouldExpireSession(HttpStatusCode.Forbidden, "token_expired")
    && !CloudSyncResponsePolicy.ShouldExpireSession(HttpStatusCode.Forbidden, "access_denied")
    && !CloudSyncResponsePolicy.ShouldExpireSession(HttpStatusCode.ServiceUnavailable, null)
    && CloudSyncResponsePolicy.ReadErrorCode(Encoding.UTF8.GetBytes("{\"error\":\"token_expired\"}")) == "token_expired"
    && CloudSyncResponsePolicy.ReadErrorCode(Encoding.UTF8.GetBytes("{\"Error\":\"access_denied\"}")) == "access_denied"
    && CloudSyncResponsePolicy.ReadErrorCode(Encoding.UTF8.GetBytes("not-json")) is null,
    "云端同步仅在 HTTP 401 或明确 Token 错误码时清除登录态");
using (var unauthorizedResponse = new HttpResponseMessage(HttpStatusCode.Unauthorized)
       { Content = new StringContent("{\"error\":\"server_error\"}") })
    Assert(await CloudSyncResponsePolicy.ShouldExpireSessionAsync(unauthorizedResponse),
        "HTTP 客户端响应级策略将任意 401 识别为会话失效");
using (var expiredResponse = new HttpResponseMessage(HttpStatusCode.Forbidden)
       { Content = new StringContent("{\"error\":\"token_expired\"}") })
    Assert(await CloudSyncResponsePolicy.ShouldExpireSessionAsync(expiredResponse),
        "HTTP 客户端响应级策略解析 403 Token 过期错误码");
using (var forbiddenResponse = new HttpResponseMessage(HttpStatusCode.Forbidden)
       { Content = new StringContent("{\"error\":\"access_denied\"}") })
    Assert(!await CloudSyncResponsePolicy.ShouldExpireSessionAsync(forbiddenResponse),
        "HTTP 客户端响应级策略不会把普通 403 权限拒绝当作登出");
using (var oversizedForbiddenResponse = new HttpResponseMessage(HttpStatusCode.Forbidden)
       { Content = new StringContent("{\"error\":\"token_expired\"}" + new string(' ', 64 * 1024)) })
    Assert(!await CloudSyncResponsePolicy.ShouldExpireSessionAsync(oversizedForbiddenResponse),
        "HTTP 客户端响应级策略拒绝解析超限 403 错误体并保留会话");
using (var serverErrorResponse = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
       { Content = new StringContent("{\"error\":\"token_expired\"}") })
    Assert(!await CloudSyncResponsePolicy.ShouldExpireSessionAsync(serverErrorResponse),
        "HTTP 客户端响应级策略不会因 5xx 错误体中的 Token 字样清除会话");
Assert(UserSessionPolicy.OwnsData("user-a", "user-a", "user-a")
    && !UserSessionPolicy.OwnsData("user-a", "user-b", "user-b")
    && !UserSessionPolicy.OwnsData("user-a", null, null),
    "异步用户数据写入仅允许原账号仍为当前登录和数据归属账号时提交");
Assert(WindowClosePolicy.ShouldHideToTray(true, true, false)
    && !WindowClosePolicy.ShouldHideToTray(false, true, false)
    && !WindowClosePolicy.ShouldHideToTray(true, false, false)
    && !WindowClosePolicy.ShouldHideToTray(true, true, true),
    "关闭到托盘仅作用于已登录且未显式退出的窗口关闭");
Assert(NavigationCollapsePolicy.ShouldToggle(true, false, false, true)
    && !NavigationCollapsePolicy.ShouldToggle(true, false, true, true)
    && !NavigationCollapsePolicy.ShouldToggle(true, false, false, false)
    && !NavigationCollapsePolicy.ShouldToggle(false, false, false, true)
    && NavigationCollapsePolicy.ShouldToggle(true, true, false, false),
    "双击仅在导航空白处收缩、交互控件/主内容区不触发且收缩栏可恢复");
Assert(UserSettingsPolicy.StorageKey("qaAssistantEnabled", "user-a") == "user:user-a:qaAssistantEnabled"
    && UserSettingsPolicy.StorageKey("qaAssistantEnabled", "user-b") == "user:user-b:qaAssistantEnabled"
    && UserSettingsPolicy.StorageKey("qaAssistantEnabled", null) is null
    && UserSettingsPolicy.StorageKey("startupEnabled", "user-a") == "startupEnabled",
    "账号偏好设置相互隔离，开机启动等机器级设置保持全局");
Assert(QaClipboardReadPolicy.CanReplaceQuestion(false)
    && !QaClipboardReadPolicy.CanReplaceQuestion(true),
    "问答页剪贴板自动读取不会覆盖用户编辑中的问题");
Assert(PasswordVisibilityPolicy.Toggle(false) && !PasswordVisibilityPolicy.Toggle(true),
    "登录密码显示/隐藏按钮可双向切换");
var clipboardWriteOrder = new List<string>();
Assert(ClipboardWritePolicy.TryWrite(
        () => clipboardWriteOrder.Add("ignore"),
        () => clipboardWriteOrder.Add("write"),
        () => clipboardWriteOrder.Add("flush"),
        () => clipboardWriteOrder.Add("cancel"))
    && clipboardWriteOrder.SequenceEqual(new[] { "ignore", "write", "flush" }),
    "应用内剪贴板写入先标记忽略，再写入并刷新");
clipboardWriteOrder.Clear();
var clipboardWriteFailed = ClipboardWritePolicy.TryWrite(
    () => clipboardWriteOrder.Add("ignore"),
    () => throw new IOException("clipboard locked"),
    () => clipboardWriteOrder.Add("flush"),
    () => clipboardWriteOrder.Add("cancel"));
Assert(!clipboardWriteFailed && clipboardWriteOrder.SequenceEqual(new[] { "ignore", "cancel" }),
    "剪贴板写入失败时撤销忽略标记，避免吞掉下一次真实变化");
clipboardWriteOrder.Clear();
var clipboardFlushFailed = ClipboardWritePolicy.TryWrite(
    () => clipboardWriteOrder.Add("ignore"),
    () => clipboardWriteOrder.Add("write"),
    () => throw new IOException("flush failed"),
    () => clipboardWriteOrder.Add("cancel"));
Assert(!clipboardFlushFailed && clipboardWriteOrder.SequenceEqual(new[] { "ignore", "write" }),
    "写入已成功但刷新失败时保留忽略标记，避免应用内结果被重新捕获");
Assert(ClipboardIgnorePolicy.ShouldIgnore(42, 42)
    && !ClipboardIgnorePolicy.ShouldIgnore(0, 42)
    && !ClipboardIgnorePolicy.ShouldIgnore(41, 42),
    "只忽略与应用写入序列号匹配的剪贴板变化，过期标记不吞掉后续用户复制");
var legacySettings = new Dictionary<string, object>
{
    ["qaAssistantEnabled"] = true,
    ["user:user-a:qaAssistantEnabled"] = false,
    ["startupEnabled"] = true
};
UserSettingsPolicy.MigrateLegacyValues(legacySettings, "user-a");
Assert((bool)legacySettings["user:user-a:qaAssistantEnabled"] == false
    && !legacySettings.ContainsKey("qaAssistantEnabled")
    && (bool)legacySettings["startupEnabled"],
    "旧全局账号偏好迁移时不覆盖已存在账号值且保留机器级设置");
Assert(AuthInputPolicy.IsPhoneInputLengthValid("+86 138-0013-8000")
    && !AuthInputPolicy.IsPhoneInputLengthValid(new string('1', AuthInputPolicy.MaxPhoneInputLength + 1)),
    "手机号输入限制长度并允许常见格式分隔符");
Assert(AuthInputPolicy.IsEmailLengthValid("a@example.com")
    && !AuthInputPolicy.IsEmailLengthValid(new string('a', AuthInputPolicy.MaxEmailLength + 1) + "@example.com"),
    "可选邮箱输入限制最大长度");
Assert(AuthInputPolicy.IsPasswordLengthValid("abc123", 6)
    && !AuthInputPolicy.IsPasswordLengthValid("short", 6)
    && !AuthInputPolicy.IsPasswordLengthValid(new string('x', AuthInputPolicy.MaxPasswordLength + 1), 6)
    && !AuthInputPolicy.IsPasswordLengthValid("abc\n123", 6),
    "密码输入限制长度并拒绝控制字符");
using (var boundedContent = new ByteArrayContent(Encoding.UTF8.GetBytes("safe")))
{
    Assert(Encoding.UTF8.GetString(await BoundedHttpContentReader.ReadBytesAsync(boundedContent, 4)) == "safe",
        "有界 HTTP 响应读取允许上限内内容");
}
var oversizedResponseRejected = false;
using (var oversizedContent = new ByteArrayContent(Encoding.UTF8.GetBytes("oversized")))
{
    try { await BoundedHttpContentReader.ReadBytesAsync(oversizedContent, 4); }
    catch (InvalidDataException) { oversizedResponseRejected = true; }
}
Assert(oversizedResponseRejected, "认证 API 的超限响应在读取前拒绝");
using (var unknownLengthContent = new UnknownLengthContent(Encoding.UTF8.GetBytes("safe")))
{
    Assert(unknownLengthContent.Headers.ContentLength is null
        && Encoding.UTF8.GetString(await BoundedHttpContentReader.ReadBytesAsync(unknownLengthContent, 4)) == "safe",
        "有界 HTTP 流读取允许无 Content-Length 的上限内响应");
}
var chunkedOversizeRejected = false;
using (var unknownLengthOversized = new UnknownLengthContent(Encoding.UTF8.GetBytes("oversized")))
{
    try { await BoundedHttpContentReader.ReadBytesAsync(unknownLengthOversized, 4); }
    catch (InvalidDataException) { chunkedOversizeRejected = true; }
}
Assert(chunkedOversizeRejected, "有界 HTTP 流读取在无 Content-Length 时逐块拒绝超限响应");
using (var cancelledContent = new ByteArrayContent(Encoding.UTF8.GetBytes("cancel")))
using (var readCancellation = new CancellationTokenSource())
{
    readCancellation.Cancel();
    var cancellationObserved = false;
    try { await BoundedHttpContentReader.ReadBytesAsync(cancelledContent, 32, readCancellation.Token); }
    catch (OperationCanceledException) { cancellationObserved = true; }
    Assert(cancellationObserved, "有界 HTTP 流读取遵守取消令牌");
}

var testRoot = Path.Combine(Path.GetTempPath(), "KuaiJian-clipboard-ledger-" + Guid.NewGuid().ToString("N"));
try
{
    var ledgerStore = new DeletedClipboardIdStore();
    var firstAccountLedger = Path.Combine(testRoot, "user-a", "deleted-clipboard-ids.json");
    var secondAccountLedger = Path.Combine(testRoot, "user-b", "deleted-clipboard-ids.json");
    await ledgerStore.SaveAsync(firstAccountLedger, new[] { "hidden-1", "hidden-1", "" });
    Assert((await ledgerStore.LoadAsync(firstAccountLedger)).SequenceEqual(new[] { "hidden-1" }),
        "本机删除清单原子保存后可重新加载且去重");
    Assert((await ledgerStore.LoadAsync(secondAccountLedger)).Count == 0,
        "删除清单按账号目录隔离");
    var corruptLedger = Path.Combine(testRoot, "user-c", "deleted-clipboard-ids.json");
    Directory.CreateDirectory(Path.GetDirectoryName(corruptLedger)!);
    await File.WriteAllTextAsync(corruptLedger, "{");
    var warnings = new List<string>();
    Assert((await ledgerStore.LoadAsync(corruptLedger, warnings.Add)).Count == 0
        && warnings.Count == 1
        && Directory.GetFiles(Path.GetDirectoryName(corruptLedger)!, "deleted-clipboard-ids.json.corrupt-*").Length == 1
        && await File.ReadAllTextAsync(corruptLedger) == "{",
        "损坏的删除清单会保留原文件、创建备份并报告提示");
}
finally
{
    if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
}

var downloadTestRoot = Path.Combine(Path.GetTempPath(), "KuaiJian-download-store-" + Guid.NewGuid().ToString("N"));
try
{
    var transferRoot = Path.Combine(downloadTestRoot, "transfer-receiver");
    var transferReceive = Path.Combine(transferRoot, "received");
    var transferStaging = Path.Combine(transferRoot, "staging");
    Directory.CreateDirectory(transferReceive);
    var transferPayload = Enumerable.Range(0, TransferChunkPolicy.ChunkSize + 17)
        .Select(value => (byte)(value % 251)).ToArray();
    var transferHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(transferPayload));
    var transferSourcePath = Path.Combine(transferRoot, "sender-payload.bin");
    await File.WriteAllBytesAsync(transferSourcePath, transferPayload);
    await using (var sender = await TransferFileSender.OpenAsync(transferSourcePath))
    {
        var firstChunk = await sender.ReadChunkAsync(0);
        var lastChunk = await sender.ReadChunkAsync(1);
        var retriedFirstChunk = await sender.ReadChunkAsync(0);
        Assert(sender.Descriptor.FileName == "sender-payload.bin"
            && sender.Descriptor.TotalBytes == transferPayload.Length
            && sender.Descriptor.Sha256 == transferHash
            && sender.Descriptor.ChunkCount == 2
            && firstChunk.Length == TransferChunkPolicy.ChunkSize
            && lastChunk.SequenceEqual(transferPayload.AsSpan(TransferChunkPolicy.ChunkSize).ToArray())
            && retriedFirstChunk.SequenceEqual(firstChunk),
            "互传发送端生成稳定元信息并可按序号重复读取原始分块");
        var concurrentWriteRejected = false;
        try { await using var writer = new FileStream(transferSourcePath, FileMode.Open, FileAccess.Write, FileShare.None); }
        catch (IOException) { concurrentWriteRejected = true; }
        Assert(concurrentWriteRejected,
            "互传发送期间锁定源文件，阻止文件变化导致哈希与分块内容不一致");
    }
    var sendCheckpointRoot = Path.Combine(transferRoot, "send-checkpoints");
    Guid resumableSendId;
    await using (var sendSession = await ResumableTransferSendSession.BeginAsync(
        transferSourcePath, sendCheckpointRoot))
    {
        resumableSendId = sendSession.Descriptor.TransferId;
        Assert((await sendSession.GetPendingChunkIndicesAsync(2)).SequenceEqual(new long[] { 0, 1 }),
            "互传发送检查点初始暴露待发送分块");
        Assert((await sendSession.GetPendingChunkIndicesAsync(1, 1)).SequenceEqual(new long[] { 1 }),
            "互传发送续传游标从指定分块开始扫描，避免重复遍历已处理前缀");
        var sentChunk = await sendSession.ReadChunkAsync(0);
        Assert(sentChunk.SequenceEqual(transferPayload.AsSpan(0, TransferChunkPolicy.ChunkSize).ToArray()),
            "互传发送会话读取的分块与稳定源文件内容一致");
        await sendSession.AcknowledgeChunkAsync(0);
        Assert(sendSession.AcknowledgedChunkCount == 1
            && (await sendSession.GetPendingChunkIndicesAsync(2)).SequenceEqual(new long[] { 1 }),
            "仅在持久化服务端确认后推进发送断点");
        var prematureCleanupRejected = false;
        try { await sendSession.RemoveCheckpointAfterRemoteCompletionAsync(); }
        catch (InvalidOperationException) { prematureCleanupRejected = true; }
        Assert(prematureCleanupRejected && File.Exists(sendSession.CheckpointPath),
            "发送未全部确认时保留检查点供续传");
    }
    await using (var resumedSend = await ResumableTransferSendSession.ResumeAsync(
        resumableSendId.ToString(), sendCheckpointRoot))
    {
        Assert(resumedSend.AcknowledgedChunkCount == 1
            && (await resumedSend.GetPendingChunkIndicesAsync(2)).SequenceEqual(new long[] { 1 }),
            "互传发送进程重启后恢复已确认分块并跳过重传");
        var lastChunk = await resumedSend.ReadChunkAsync(1);
        Assert(lastChunk.SequenceEqual(transferPayload.AsSpan(TransferChunkPolicy.ChunkSize).ToArray()),
            "互传发送恢复后可重读未确认的末尾分块");
        await resumedSend.AcknowledgeChunkAsync(1);
        Assert(resumedSend.IsFullyAcknowledged && resumedSend.AcknowledgedChunkCount == 2,
            "全部发送分块确认后状态正确完成");
        await resumedSend.RemoveCheckpointAfterRemoteCompletionAsync();
        Assert(!File.Exists(resumedSend.CheckpointPath),
            "远端完成确认后清除发送检查点");
    }

    await using (var sparseProgressSession = await ResumableTransferSendSession.BeginAsync(
        transferSourcePath, sendCheckpointRoot))
    {
        await sparseProgressSession.AcknowledgeChunkAsync(1);
        Assert(sparseProgressSession.AcknowledgedChunkCount == 1
            && sparseProgressSession.AcknowledgedByteCount == 17,
            "互传发送进度按实际稀疏短尾块字节数计算");
        await sparseProgressSession.AcknowledgeChunkAsync(0);
        await sparseProgressSession.RemoveCheckpointAfterRemoteCompletionAsync();
    }

    var changedSource = Path.Combine(transferRoot, "changed-source.bin");
    await File.WriteAllBytesAsync(changedSource, new byte[] { 1, 2, 3 });
    Guid changedTransferId;
    await using (var changedSession = await ResumableTransferSendSession.BeginAsync(
        changedSource, sendCheckpointRoot))
        changedTransferId = changedSession.Descriptor.TransferId;
    await File.WriteAllBytesAsync(changedSource, new byte[] { 1, 2, 4 });
    var changedSourceRejected = false;
    try { await using var _ = await ResumableTransferSendSession.ResumeAsync(
        changedTransferId.ToString(), sendCheckpointRoot); }
    catch (InvalidDataException) { changedSourceRejected = true; }
    Assert(changedSourceRejected && File.Exists(Path.Combine(sendCheckpointRoot,
            changedTransferId.ToString("N") + ".send.json")),
        "发送源文件改变时拒绝续传并保留原检查点供用户处理");

    var ackFailureSource = Path.Combine(transferRoot, "ack-failure-source.bin");
    await File.WriteAllBytesAsync(ackFailureSource, new byte[] { 4, 5, 6 });
    await using (var ackFailureSession = await ResumableTransferSendSession.BeginAsync(
        ackFailureSource, sendCheckpointRoot))
    {
        var brokenSendCheckpointTemp = ackFailureSession.CheckpointPath + ".tmp";
        Directory.CreateDirectory(brokenSendCheckpointTemp);
        var ackPersistenceFailed = false;
        try { await ackFailureSession.AcknowledgeChunkAsync(0); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { ackPersistenceFailed = true; }
        Assert(ackPersistenceFailed && ackFailureSession.AcknowledgedChunkCount == 0,
            "发送 ack 检查点写入失败时回滚已确认计数");
        Directory.Delete(brokenSendCheckpointTemp);
        await ackFailureSession.AcknowledgeChunkAsync(0);
        Assert(ackFailureSession.IsFullyAcknowledged,
            "发送 ack 检查点恢复可写后允许同一分块重新确认");
        await ackFailureSession.RemoveCheckpointAfterRemoteCompletionAsync();
    }

    var coordinatedSource = Path.Combine(transferRoot, "coordinated-source.bin");
    await File.WriteAllBytesAsync(coordinatedSource, transferPayload);
    await using (var coordinatedSession = await ResumableTransferSendSession.BeginAsync(
        coordinatedSource, sendCheckpointRoot))
    {
        var transport = new FakeTransferSendTransport();
        var progressUpdates = new List<TransferSendProgress>();
        await TransferSendCoordinator.SendAsync(coordinatedSession, transport,
            new InlineProgress<TransferSendProgress>(progressUpdates.Add));
        Assert(transport.BeginCount == 1
            && transport.UploadedIndices.SequenceEqual(new long[] { 0, 1 })
            && coordinatedSession.IsFullyAcknowledged
            && !File.Exists(coordinatedSession.CheckpointPath)
            && progressUpdates.Count == 2
            && progressUpdates[^1].AcknowledgedBytes == transferPayload.Length,
            "互传协调器按序上传、校验确认、报告进度并在远端提交后清理检查点");
    }

    await using (var retryCoordinatorSession = await ResumableTransferSendSession.BeginAsync(
        coordinatedSource, sendCheckpointRoot))
    {
        var retryTransport = new FakeTransferSendTransport { FailAfterStoringIndexOnce = 0 };
        var firstAttemptFailed = false;
        try { await TransferSendCoordinator.SendAsync(retryCoordinatorSession, retryTransport); }
        catch (IOException) { firstAttemptFailed = true; }
        Assert(firstAttemptFailed
            && retryCoordinatorSession.AcknowledgedChunkCount == 0
            && File.Exists(retryCoordinatorSession.CheckpointPath),
            "互传上传后确认丢失时保留本地检查点并允许幂等重试");
        await TransferSendCoordinator.SendAsync(retryCoordinatorSession, retryTransport);
        Assert(retryTransport.UploadedIndices.SequenceEqual(new long[] { 0, 0, 1 })
            && !File.Exists(retryCoordinatorSession.CheckpointPath),
            "互传重试复用稳定任务 ID，重复分块不覆盖并最终完成");
    }

    await using (var invalidAckSession = await ResumableTransferSendSession.BeginAsync(
        coordinatedSource, sendCheckpointRoot))
    {
        var invalidAckTransport = new FakeTransferSendTransport { WrongAcknowledgementOnce = true };
        var invalidAckRejected = false;
        try { await TransferSendCoordinator.SendAsync(invalidAckSession, invalidAckTransport); }
        catch (InvalidDataException) { invalidAckRejected = true; }
        Assert(invalidAckRejected
            && invalidAckSession.AcknowledgedChunkCount == 0
            && File.Exists(invalidAckSession.CheckpointPath),
            "互传确认的任务 ID/分块序号不匹配时不推进检查点");
    }

    await using (var incompleteCommitSession = await ResumableTransferSendSession.BeginAsync(
        coordinatedSource, sendCheckpointRoot))
    {
        var incompleteTransport = new FakeTransferSendTransport { ReportSaved = false };
        var incompleteCommitRejected = false;
        try { await TransferSendCoordinator.SendAsync(incompleteCommitSession, incompleteTransport); }
        catch (IOException) { incompleteCommitRejected = true; }
        Assert(incompleteCommitRejected
            && incompleteCommitSession.IsFullyAcknowledged
            && File.Exists(incompleteCommitSession.CheckpointPath),
            "远端未确认完整保存时保留全部已确认分块的发送检查点");
    }

    await using (var wrongCompletionHashSession = await ResumableTransferSendSession.BeginAsync(
        coordinatedSource, sendCheckpointRoot))
    {
        var wrongHashTransport = new FakeTransferSendTransport
        {
            CompletionHashOverride = new string('0', 64)
        };
        var wrongCompletionHashRejected = false;
        try { await TransferSendCoordinator.SendAsync(wrongCompletionHashSession, wrongHashTransport); }
        catch (IOException) { wrongCompletionHashRejected = true; }
        Assert(wrongCompletionHashRejected
            && wrongCompletionHashSession.IsFullyAcknowledged
            && File.Exists(wrongCompletionHashSession.CheckpointPath),
            "远端最终 SHA-256 不匹配时不清理发送检查点");
    }

    var emptyTransferSource = Path.Combine(transferRoot, "empty-transfer-source.bin");
    await File.WriteAllBytesAsync(emptyTransferSource, Array.Empty<byte>());
    await using (var emptySendSession = await ResumableTransferSendSession.BeginAsync(
        emptyTransferSource, sendCheckpointRoot))
    {
        var emptyTransport = new FakeTransferSendTransport();
        await TransferSendCoordinator.SendAsync(emptySendSession, emptyTransport);
        Assert(emptySendSession.IsFullyAcknowledged
            && emptyTransport.UploadedIndices.Count == 0
            && emptyTransport.BeginCount == 1
            && !File.Exists(emptySendSession.CheckpointPath),
            "互传零字节文件不发送伪造分块，远端确认后正常完成");
    }

    var coordinatedReceiveDirectory = Path.Combine(transferRoot, "coordinated-received");
    var coordinatedReceiveStaging = Path.Combine(transferRoot, "coordinated-receive-staging");
    Directory.CreateDirectory(coordinatedReceiveDirectory);
    var receiveDescriptor = new TransferFileDescriptor(Guid.NewGuid(), "incoming.bin", transferPayload.Length,
        transferHash, TransferChunkPolicy.GetChunkCount(transferPayload.Length));
    var receiveTransport = new FakeTransferReceiveTransport(receiveDescriptor, transferPayload);
    var receiveProgress = new List<TransferReceiveProgress>();
    var coordinatedSavedPath = await TransferReceiveCoordinator.ReceiveAsync(receiveDescriptor,
        coordinatedReceiveDirectory, coordinatedReceiveStaging, receiveTransport,
        new InlineProgress<TransferReceiveProgress>(receiveProgress.Add));
    Assert(File.ReadAllBytes(coordinatedSavedPath).SequenceEqual(transferPayload)
        && receiveTransport.AcknowledgementCount == 1
        && receiveTransport.AcknowledgementSawSavedFile
        && receiveProgress.Count == 2
        && !Directory.Exists(Path.Combine(coordinatedReceiveStaging, receiveDescriptor.TransferId.ToString("N"))),
        "互传接收协调器先持久接收并安全落盘，再确认服务端并报告进度");

    var ackRetryDescriptor = new TransferFileDescriptor(Guid.NewGuid(), "ack-retry.bin", transferPayload.Length,
        transferHash, TransferChunkPolicy.GetChunkCount(transferPayload.Length));
    var ackRetryTransport = new FakeTransferReceiveTransport(ackRetryDescriptor, transferPayload)
    {
        FailAcknowledgementOnce = true
    };
    var ackRetryReceiveDirectory = Path.Combine(transferRoot, "ack-retry-received");
    var ackRetryStaging = Path.Combine(transferRoot, "ack-retry-staging");
    var receiveAckFailed = false;
    try
    {
        await TransferReceiveCoordinator.ReceiveAsync(ackRetryDescriptor,
            ackRetryReceiveDirectory, ackRetryStaging, ackRetryTransport);
    }
    catch (IOException) { receiveAckFailed = true; }
    var savedFileCountAfterAckFailure = Directory.GetFiles(ackRetryReceiveDirectory).Length;
    var downloadedChunksAfterAckFailure = ackRetryTransport.DownloadedIndices.Count;
    Assert(receiveAckFailed
        && savedFileCountAfterAckFailure == 1
        && File.Exists(Path.Combine(ackRetryStaging, "receive-ack-outbox",
            ackRetryDescriptor.TransferId.ToString("N") + ".json")),
        "接收成功但服务端 ack 响应丢失时保留持久化确认 outbox");
    var ackRetryPath = await TransferReceiveCoordinator.ReceiveAsync(ackRetryDescriptor,
        ackRetryReceiveDirectory, ackRetryStaging, ackRetryTransport);
    Assert(File.Exists(ackRetryPath)
        && Directory.GetFiles(ackRetryReceiveDirectory).Length == savedFileCountAfterAckFailure
        && ackRetryTransport.DownloadedIndices.Count == downloadedChunksAfterAckFailure
        && ackRetryTransport.AcknowledgementCount == 2
        && !File.Exists(Path.Combine(ackRetryStaging, "receive-ack-outbox",
            ackRetryDescriptor.TransferId.ToString("N") + ".json")),
        "接收 ack 重试复用已保存文件，不重复下载或生成副本");

    var invalidReceiveDescriptor = new TransferFileDescriptor(Guid.NewGuid(), "invalid-chunk.bin",
        transferPayload.Length, transferHash, TransferChunkPolicy.GetChunkCount(transferPayload.Length));
    var invalidReceiveTransport = new FakeTransferReceiveTransport(invalidReceiveDescriptor, transferPayload)
    {
        ReturnWrongChunkIdOnce = true
    };
    var invalidReceiveRejected = false;
    try
    {
        await TransferReceiveCoordinator.ReceiveAsync(invalidReceiveDescriptor,
            Path.Combine(transferRoot, "invalid-received"), Path.Combine(transferRoot, "invalid-staging"),
            invalidReceiveTransport);
    }
    catch (InvalidDataException) { invalidReceiveRejected = true; }
    Assert(invalidReceiveRejected && invalidReceiveTransport.AcknowledgementCount == 0
        && Directory.Exists(Path.Combine(transferRoot, "invalid-staging",
            invalidReceiveDescriptor.TransferId.ToString("N"))),
        "接收分块任务 ID 不匹配时不 ack 且保留检查点供重试");

    foreach (var malformedChunkCase in new[] { "short", "overlong" })
    {
        var malformedDescriptor = new TransferFileDescriptor(Guid.NewGuid(),
            malformedChunkCase + "-chunk.bin", transferPayload.Length, transferHash,
            TransferChunkPolicy.GetChunkCount(transferPayload.Length));
        var malformedTransport = new FakeTransferReceiveTransport(malformedDescriptor, transferPayload)
        {
            ReturnShortChunkOnce = malformedChunkCase == "short",
            ReturnOverlongChunkOnce = malformedChunkCase == "overlong"
        };
        var malformedRejected = false;
        try
        {
            await TransferReceiveCoordinator.ReceiveAsync(malformedDescriptor,
                Path.Combine(transferRoot, malformedChunkCase + "-chunk-received"),
                Path.Combine(transferRoot, malformedChunkCase + "-chunk-staging"), malformedTransport);
        }
        catch (Exception exception) when (exception is EndOfStreamException or InvalidDataException)
        { malformedRejected = true; }
        Assert(malformedRejected && malformedTransport.AcknowledgementCount == 0,
            malformedChunkCase == "short"
                ? "互传短分块响应在 ack 前拒绝并保留本地接收状态"
                : "互传超长分块响应在 ack 前拒绝且不会无限读取入内存");
    }

    var partialReceiveDescriptor = new TransferFileDescriptor(Guid.NewGuid(), "partial-retry.bin",
        transferPayload.Length, transferHash, TransferChunkPolicy.GetChunkCount(transferPayload.Length));
    var partialReceiveTransport = new FakeTransferReceiveTransport(partialReceiveDescriptor, transferPayload)
    {
        FailDownloadIndexOnce = 1
    };
    var partialReceiveDirectory = Path.Combine(transferRoot, "partial-retry-received");
    var partialReceiveStaging = Path.Combine(transferRoot, "partial-retry-staging");
    var partialDownloadFailed = false;
    try
    {
        await TransferReceiveCoordinator.ReceiveAsync(partialReceiveDescriptor,
            partialReceiveDirectory, partialReceiveStaging, partialReceiveTransport);
    }
    catch (IOException) { partialDownloadFailed = true; }
    Assert(partialDownloadFailed
        && partialReceiveTransport.DownloadedIndices.SequenceEqual(new long[] { 0, 1 })
        && File.Exists(Path.Combine(partialReceiveStaging, partialReceiveDescriptor.TransferId.ToString("N"), "checkpoint.json")),
        "互传接收网络中断时保留此前已持久化分块检查点");
    var partialReceivePath = await TransferReceiveCoordinator.ReceiveAsync(partialReceiveDescriptor,
        partialReceiveDirectory, partialReceiveStaging, partialReceiveTransport);
    Assert(File.ReadAllBytes(partialReceivePath).SequenceEqual(transferPayload)
        && partialReceiveTransport.DownloadedIndices.SequenceEqual(new long[] { 0, 1, 1 })
        && partialReceiveTransport.AcknowledgementCount == 1,
        "互传接收重启后只重试未持久化分块并完成校验");

    var recoveredReceiveDirectory = Path.Combine(transferRoot, "recovered-received");
    Directory.CreateDirectory(recoveredReceiveDirectory);
    var recoveredExistingPath = Path.Combine(recoveredReceiveDirectory, "crash-gap.bin");
    await File.WriteAllBytesAsync(recoveredExistingPath, transferPayload);
    var recoveredDescriptor = new TransferFileDescriptor(Guid.NewGuid(), "crash-gap.bin", transferPayload.Length,
        transferHash, TransferChunkPolicy.GetChunkCount(transferPayload.Length));
    var recoveredTransport = new FakeTransferReceiveTransport(recoveredDescriptor, transferPayload);
    var recoveredPath = await TransferReceiveCoordinator.ReceiveAsync(recoveredDescriptor,
        recoveredReceiveDirectory, Path.Combine(transferRoot, "recovered-staging"), recoveredTransport);
    Assert(recoveredPath == recoveredExistingPath
        && recoveredTransport.DownloadedIndices.Count == 0
        && recoveredTransport.AcknowledgementCount == 1,
        "提交文件后进程中断、确认 outbox 落盘前重启时按文件哈希恢复并确认");

    await File.WriteAllTextAsync(Path.Combine(transferReceive, "payload.bin"), "keep-existing");
    var transferId = Guid.NewGuid().ToString("D");
    var receiver = await ResumableTransferReceiver.BeginAsync(transferId, "payload.bin", transferPayload.Length,
        transferHash, transferReceive, transferStaging);
    Assert((await receiver.GetPendingChunkIndicesAsync(1, 1)).SequenceEqual(new long[] { 1 }),
        "互传接收续传游标可从指定分块开始读取未接收列表");
    var wrongLengthRejected = false;
    try { await receiver.ReceiveChunkAsync(0, new byte[1]); }
    catch (InvalidDataException) { wrongLengthRejected = true; }
    Assert(wrongLengthRejected && receiver.ReceivedChunkCount == 0,
        "互传拒绝长度不符的分块且不提前写入接收检查点");
    await receiver.ReceiveChunkAsync(1, transferPayload.AsMemory(TransferChunkPolicy.ChunkSize));
    Assert(receiver.ReceivedChunkCount == 1 && receiver.ReceivedByteCount == 17,
        "互传接收进度按实际乱序短尾块字节数计算");
    await receiver.ReceiveChunkAsync(0, transferPayload.AsMemory(0, TransferChunkPolicy.ChunkSize));
    await receiver.ReceiveChunkAsync(0, transferPayload.AsMemory(0, TransferChunkPolicy.ChunkSize));
    Assert(receiver.ReceivedChunkCount == 2,
        "互传支持乱序分块和重试，重复分块不重复计数");
    var resumedReceiver = await ResumableTransferReceiver.ResumeAsync(transferId, transferReceive, transferStaging);
    Assert(resumedReceiver.ReceivedChunkCount == 2,
        "互传检查点重启恢复已持久化的分块状态");
    var incompleteRejected = false;
    var incompleteId = Guid.NewGuid().ToString("D");
    var incompleteReceiver = await ResumableTransferReceiver.BeginAsync(incompleteId, "partial.bin",
        transferPayload.Length, transferHash, transferReceive, transferStaging);
    try { await incompleteReceiver.CompleteAsync(); }
    catch (InvalidOperationException) { incompleteRejected = true; }
    Assert(incompleteRejected && File.Exists(Path.Combine(transferStaging, Guid.Parse(incompleteId).ToString("N"), "payload.part")),
        "互传未收齐分块时禁止提交文件，保留断点供后续续传");
    var completedTransfer = await resumedReceiver.CompleteAsync();
    Assert(File.ReadAllBytes(completedTransfer).SequenceEqual(transferPayload)
        && Path.GetFileName(completedTransfer) == "payload (1).bin"
        && File.ReadAllText(Path.Combine(transferReceive, "payload.bin")) == "keep-existing"
        && !Directory.Exists(Path.Combine(transferStaging, Guid.Parse(transferId).ToString("N"))),
        "互传先校验 SHA-256 再提交完整文件，冲突改名且不覆盖原文件");

    var badHashId = Guid.NewGuid().ToString("D");
    var badHashReceiver = await ResumableTransferReceiver.BeginAsync(badHashId, "bad-hash.bin",
        transferPayload.Length, new string('0', 64), transferReceive, transferStaging);
    await badHashReceiver.ReceiveChunkAsync(0, transferPayload.AsMemory(0, TransferChunkPolicy.ChunkSize));
    await badHashReceiver.ReceiveChunkAsync(1, transferPayload.AsMemory(TransferChunkPolicy.ChunkSize));
    var hashMismatchRejected = false;
    try { await badHashReceiver.CompleteAsync(); }
    catch (InvalidDataException) { hashMismatchRejected = true; }
    Assert(hashMismatchRejected && !File.Exists(Path.Combine(transferReceive, "bad-hash.bin")),
        "互传完整哈希不匹配时拒绝保存文件，不得确认接收");

    var protectedId = Guid.NewGuid();
    var protectedStage = Path.Combine(transferStaging, protectedId.ToString("N"));
    Directory.CreateDirectory(protectedStage);
    var protectedPart = Path.Combine(protectedStage, "payload.part");
    await File.WriteAllTextAsync(protectedPart, "preserve-existing-checkpoint");
    var existingTransferRejected = false;
    try { await ResumableTransferReceiver.BeginAsync(protectedId.ToString(), "new.bin", 0,
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Array.Empty<byte>())),
        transferReceive, transferStaging); }
    catch (IOException) { existingTransferRejected = true; }
    Assert(existingTransferRejected && await File.ReadAllTextAsync(protectedPart) == "preserve-existing-checkpoint",
        "重复创建互传检查点时拒绝覆盖或清理已有临时数据");

    var retryPayload = new byte[] { 7, 19, 31 };
    var retryId = Guid.NewGuid();
    var retryReceiver = await ResumableTransferReceiver.BeginAsync(retryId.ToString(), "retry.bin",
        retryPayload.Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(retryPayload)),
        transferReceive, transferStaging);
    var brokenCheckpointTemp = Path.Combine(transferStaging, retryId.ToString("N"), "checkpoint.json.tmp");
    Directory.CreateDirectory(brokenCheckpointTemp);
    var checkpointWriteFailed = false;
    try { await retryReceiver.ReceiveChunkAsync(0, retryPayload); }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    { checkpointWriteFailed = true; }
    Assert(checkpointWriteFailed && retryReceiver.ReceivedChunkCount == 0,
        "互传检查点写入失败时不提前报告分块已持久化");
    Directory.Delete(brokenCheckpointTemp);
    await retryReceiver.ReceiveChunkAsync(0, retryPayload);
    var retryOutput = await retryReceiver.CompleteAsync();
    var retryResult = await File.ReadAllBytesAsync(retryOutput);
    Assert(retryResult.SequenceEqual(retryPayload),
        "检查点写入失败后的分块可安全重试并完成接收");

    var emptyId = Guid.NewGuid().ToString("D");
    var emptyReceiver = await ResumableTransferReceiver.BeginAsync(emptyId, "empty.bin", 0,
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Array.Empty<byte>())),
        transferReceive, transferStaging);
    var emptyOutput = await emptyReceiver.CompleteAsync();
    Assert(new FileInfo(emptyOutput).Length == 0,
        "互传可校验并提交零字节文件");

    var reservationDirectory = Path.Combine(downloadTestRoot, "reserved");
    var reservedDownloads = await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() =>
    {
        var content = $"reservation-{index}";
        var reservation = DownloadFileStore.CreateUniquePartFile(reservationDirectory, "同名文件.txt");
        reservation.Stream.Write(Encoding.UTF8.GetBytes(content));
        reservation.Stream.Dispose();
        var finalPath = DownloadFileStore.MoveToUniquePath(
            reservation.PartPath, reservationDirectory, Path.GetFileName(reservation.DestinationPath));
        return (finalPath, content);
    })));
    Assert(reservedDownloads.Select(item => item.finalPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 12
        && reservedDownloads.All(item => File.ReadAllText(item.finalPath) == item.content),
        "并发同名下载通过原子临时文件预留保留全部内容且生成唯一文件名");

    var moveSourceDirectory = Path.Combine(downloadTestRoot, "sources");
    var moveDestinationDirectory = Path.Combine(downloadTestRoot, "moved");
    Directory.CreateDirectory(moveSourceDirectory);
    var moveSources = Enumerable.Range(0, 12).Select(index =>
    {
        var source = Path.Combine(moveSourceDirectory, $"source-{index}.tmp");
        var content = $"move-{index}";
        File.WriteAllText(source, content);
        return (source, content);
    }).ToArray();
    var movedDownloads = await Task.WhenAll(moveSources.Select(item => Task.Run(() =>
        (DownloadFileStore.MoveToUniquePath(item.source, moveDestinationDirectory, "同名文件.txt"), item.content))));
    Assert(movedDownloads.Select(item => item.Item1).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 12
        && movedDownloads.All(item => File.ReadAllText(item.Item1) == item.content),
        "并发提交同名文件时自动重试且不会覆盖已完成文件");

    var stagedSource = Path.Combine(downloadTestRoot, "account-partial.bin");
    var stagedDestination = Path.Combine(downloadTestRoot, "staged-output");
    var stagedPayload = Enumerable.Range(0, 256 * 1024).Select(value => (byte)(value % 251)).ToArray();
    await File.WriteAllBytesAsync(stagedSource, stagedPayload);
    Directory.CreateDirectory(stagedDestination);
    await File.WriteAllTextAsync(Path.Combine(stagedDestination, "staged.bin"), "keep-existing");
    var stagedPath = await DownloadFileStore.CopyToUniquePathAsync(stagedSource, stagedDestination, "staged.bin");
    Assert(File.ReadAllBytes(stagedPath).SequenceEqual(stagedPayload)
        && !File.Exists(stagedSource)
        && File.ReadAllText(Path.Combine(stagedDestination, "staged.bin")) == "keep-existing"
        && Path.GetFileName(stagedPath) == "staged (1).bin",
        "跨卷式复制先写目标卷临时文件、原子提交且不覆盖同名文件");

    var cancelledCopySource = Path.Combine(downloadTestRoot, "cancelled-copy.bin");
    await File.WriteAllBytesAsync(cancelledCopySource, stagedPayload);
    using var cancelledCopy = new CancellationTokenSource();
    cancelledCopy.Cancel();
    var copyCancellationObserved = false;
    try { await DownloadFileStore.CopyToUniquePathAsync(cancelledCopySource, stagedDestination, "cancelled.bin", cancelledCopy.Token); }
    catch (OperationCanceledException) { copyCancellationObserved = true; }
    Assert(copyCancellationObserved && File.Exists(cancelledCopySource)
        && !File.Exists(Path.Combine(stagedDestination, "cancelled.bin.part")),
        "跨卷目标复制启动前取消时保留源部分文件且不创建目标临时文件");

    var sourceVolume = Path.GetPathRoot(Path.GetFullPath(downloadTestRoot));
    var otherVolume = DriveInfo.GetDrives().FirstOrDefault(drive => drive.IsReady
        && !string.Equals(drive.RootDirectory.FullName, sourceVolume, StringComparison.OrdinalIgnoreCase));
    if (otherVolume is not null)
    {
        var crossVolumeRoot = Path.Combine(otherVolume.RootDirectory.FullName,
            "KuaiJian-cross-volume-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var crossSourceDirectory = Path.Combine(crossVolumeRoot, "source");
            var crossDestinationDirectory = Path.Combine(crossVolumeRoot, "destination");
            Directory.CreateDirectory(crossSourceDirectory);
            var crossSource = Path.Combine(crossSourceDirectory, "account-partial.bin");
            await File.WriteAllBytesAsync(crossSource, stagedPayload);
            var crossOutput = await DownloadFileStore.MoveToUniquePathAsync(
                crossSource, crossDestinationDirectory, "cross-volume.bin");
            Assert(File.ReadAllBytes(crossOutput).SequenceEqual(stagedPayload) && !File.Exists(crossSource),
                "不同磁盘卷间先复制到目标卷临时文件再提交，最终字节一致");
        }
        finally
        {
            if (Directory.Exists(crossVolumeRoot)) Directory.Delete(crossVolumeRoot, recursive: true);
        }
    }
}
finally
{
    if (Directory.Exists(downloadTestRoot)) Directory.Delete(downloadTestRoot, recursive: true);
}

var powershellPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
var processStartInfo = File.Exists(powershellPath)
    ? new ProcessStartInfo(powershellPath)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        ArgumentList = { "-NoProfile", "-Command", "Start-Sleep -Seconds 30" }
    }
    : new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"))
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        ArgumentList = { "/c", "ping", "-n", "30", "127.0.0.1" }
    };
using (var child = new Process
{
    StartInfo = processStartInfo
})
{
    if (!child.Start()) throw new InvalidOperationException("Could not start cancellation test child process.");
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
    var cancellationObserved = false;
    try { await CancellableProcessWait.WaitForExitAsync(child, cancellation.Token); }
    catch (OperationCanceledException) { cancellationObserved = true; }
    Assert(cancellationObserved && child.HasExited,
        "取消下载时等待子进程树退出后再开始清理");
}

Console.WriteLine($"全部 {assertionsPassed} 项离线回归通过。");

sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

sealed class FakeTransferSendTransport : ITransferSendTransport
{
    private TransferFileDescriptor? _descriptor;
    private bool _failureInjected;
    private bool _badAcknowledgementInjected;
    private readonly Dictionary<long, byte[]> _chunks = new();

    public int BeginCount { get; private set; }
    public List<long> UploadedIndices { get; } = new();
    public long? FailAfterStoringIndexOnce { get; init; }
    public bool WrongAcknowledgementOnce { get; init; }
    public bool ReportSaved { get; init; } = true;
    public string? CompletionHashOverride { get; init; }

    public Task BeginAsync(TransferFileDescriptor descriptor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_descriptor is not null && _descriptor != descriptor)
            throw new InvalidDataException("测试传输任务元信息发生变化。");
        _descriptor = descriptor;
        BeginCount++;
        return Task.CompletedTask;
    }

    public Task<TransferChunkAcknowledgement> UploadChunkAsync(
        TransferChunkUpload chunk, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var descriptor = _descriptor ?? throw new InvalidOperationException("尚未建立测试传输任务。");
        if (chunk.TransferId != descriptor.TransferId
            || !KuaiJian.WinUI.TransferChunkPolicy.IsValidSha256(chunk.Sha256)
            || !string.Equals(chunk.Sha256,
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(chunk.Data.Span)),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("测试传输分块元信息无效。");

        if (_chunks.TryGetValue(chunk.Index, out var prior)
            && !prior.AsSpan().SequenceEqual(chunk.Data.Span))
            throw new InvalidDataException("同一分块序号不能对应不同内容。");
        _chunks[chunk.Index] = chunk.Data.ToArray();
        UploadedIndices.Add(chunk.Index);

        if (!_failureInjected && FailAfterStoringIndexOnce == chunk.Index)
        {
            _failureInjected = true;
            throw new IOException("模拟：服务端已持久化分块，但响应在客户端收到前丢失。");
        }

        if (!_badAcknowledgementInjected && WrongAcknowledgementOnce)
        {
            _badAcknowledgementInjected = true;
            return Task.FromResult(new TransferChunkAcknowledgement(chunk.TransferId, chunk.Index + 1));
        }

        return Task.FromResult(new TransferChunkAcknowledgement(chunk.TransferId, chunk.Index));
    }

    public Task<TransferCompletionAcknowledgement> CompleteAsync(
        TransferFileDescriptor descriptor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_descriptor != descriptor) throw new InvalidDataException("测试完成请求任务不匹配。");
        using var content = new MemoryStream();
        for (long index = 0; index < descriptor.ChunkCount; index++)
        {
            if (!_chunks.TryGetValue(index, out var chunk))
                return Task.FromResult(new TransferCompletionAcknowledgement(
                    descriptor.TransferId, descriptor.Sha256, false));
            content.Write(chunk);
        }
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content.ToArray()));
        return Task.FromResult(new TransferCompletionAcknowledgement(
            descriptor.TransferId, CompletionHashOverride ?? hash, ReportSaved));
    }
}

sealed class FakeTransferReceiveTransport(
    TransferFileDescriptor descriptor,
    byte[] payload) : ITransferReceiveTransport
{
    private bool _wrongChunkIdInjected;
    private bool _ackFailureInjected;

    public List<long> DownloadedIndices { get; } = new();
    public int AcknowledgementCount { get; private set; }
    public bool AcknowledgementSawSavedFile { get; private set; }
    public bool FailAcknowledgementOnce { get; init; }
    public bool ReturnWrongChunkIdOnce { get; init; }
    public long? FailDownloadIndexOnce { get; init; }
    public bool ReturnShortChunkOnce { get; init; }
    public bool ReturnOverlongChunkOnce { get; init; }
    private bool _downloadFailureInjected;
    private bool _malformedChunkInjected;

    public Task<TransferDownloadedChunk> DownloadChunkAsync(
        TransferFileDescriptor request, long index, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request != descriptor) throw new InvalidDataException("测试接收任务元信息不匹配。");
        var length = TransferChunkPolicy.GetChunkLength(descriptor.TotalBytes, index);
        var bytes = new byte[length];
        if (length > 0) Array.Copy(payload, checked((int)TransferChunkPolicy.GetChunkOffset(index)), bytes, 0, length);
        DownloadedIndices.Add(index);
        if (!_downloadFailureInjected && FailDownloadIndexOnce == index)
        {
            _downloadFailureInjected = true;
            throw new IOException("模拟：接收下一分块时网络中断。");
        }
        if (!_wrongChunkIdInjected && ReturnWrongChunkIdOnce)
        {
            _wrongChunkIdInjected = true;
            return Task.FromResult(new TransferDownloadedChunk(Guid.NewGuid(), index,
                new MemoryStream(bytes, writable: false)));
        }
        if (!_malformedChunkInjected && ReturnShortChunkOnce && bytes.Length > 0)
        {
            _malformedChunkInjected = true;
            bytes = bytes[..^1];
        }
        else if (!_malformedChunkInjected && ReturnOverlongChunkOnce)
        {
            _malformedChunkInjected = true;
            bytes = bytes.Append((byte)0xA5).ToArray();
        }
        return Task.FromResult(new TransferDownloadedChunk(descriptor.TransferId, index,
            new MemoryStream(bytes, writable: false)));
    }

    public Task<TransferReceiveAcknowledgement> AcknowledgeSavedAsync(
        TransferFileDescriptor request, string savedPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request != descriptor) throw new InvalidDataException("测试接收 ack 元信息不匹配。");
        AcknowledgementCount++;
        AcknowledgementSawSavedFile |= File.Exists(savedPath)
            && new FileInfo(savedPath).Length == descriptor.TotalBytes;
        if (!AcknowledgementSawSavedFile)
            throw new InvalidOperationException("接收协调器必须先保存文件再 ack。");
        if (!_ackFailureInjected && FailAcknowledgementOnce)
        {
            _ackFailureInjected = true;
            throw new IOException("模拟：服务端已执行 ack，但响应丢失。");
        }
        return Task.FromResult(new TransferReceiveAcknowledgement(
            descriptor.TransferId, descriptor.Sha256, Acknowledged: true));
    }
}

record SyncRecord(string Id, string Text);
record RetentionRecord(string Id, DateTimeOffset CreatedAt, bool Pinned);

sealed class UnknownLengthContent(byte[] data) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
        stream.WriteAsync(data).AsTask();

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}

sealed class DelegateHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        send(request, cancellationToken);
}
