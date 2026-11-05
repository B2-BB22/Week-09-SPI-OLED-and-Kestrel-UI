using ESP32.Kestrel.Webserver.Services;

var builder = WebApplication.CreateBuilder(args);

// ลงทะเบียน CalibrationService เป็น Singleton
builder.Services.AddSingleton<CalibrationService>();

var app = builder.Build();

// Route 1: อ่านข้อมูล Telemetry ปัจจุบัน
app.MapGet("/api/telemetry", (CalibrationService cal) =>
{
    int simulatedRaw = 2048; // ค่าดิบจำลอง หรืออ่านจากฮาร์ดแวร์จริง
    double calibrated = cal.Compute(simulatedRaw);
    return Results.Ok(new
    {
        studentId = "67030281",
        raw = simulatedRaw,
        calibrated = Math.Round(calibrated, 1),
        unit = cal.Settings.Unit,
        displayMsg = cal.CurrentOledMessage,
        timestamp = DateTime.UtcNow
    });
});

// Route 2: ปรับเทียบเซนเซอร์เชิงเส้นแบบสองจุด (Two-Point Linear Calibration)
app.MapPost("/api/potentiometer/calibrate", (CalibrationSettings newSettings, CalibrationService cal) =>
{
    try
    {
        cal.UpdateSettings(newSettings);
        cal.SetOledMessage("CALIBRATED OK");
        return Results.Ok(new { status = "success", settings = cal.Settings });
    }
    catch (ArgumentException ex)
    {
        // ป้องกัน Server Crash และส่งคืน 400 Bad Request
        return Results.BadRequest(new { status = "error", message = ex.Message });
    }
});

// Route 3: สั่งส่งข้อความควบคุมขึ้นหน้าจอ OLED
app.MapPost("/api/oled/message", (DisplayMessageRequest req, CalibrationService cal) =>
{
    if (string.IsNullOrWhiteSpace(req.Message))
    {
        return Results.BadRequest(new { status = "error", message = "ข้อความต้องไม่ว่างเปล่า" });
    }
    cal.SetOledMessage(req.Message);
    return Results.Ok(new { status = "success", current = cal.CurrentOledMessage });
});

app.Run();

