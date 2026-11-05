# รายงานผลการทดลองและเฉลยคำตอบ ใบงานที่ 9.2 (Lab 9.2)
## การพัฒนาเอนจินปรับเทียบเซนเซอร์และ API ควบคุมการแสดงผลบน Kestrel Web Server พร้อมการพิสูจน์หลักฐานเครือข่าย (HTTP Payload Forensics)

**รหัสนักศึกษา:** 67030281    
**สัปดาห์ที่:** 09 - SPI OLED Display Architecture, 1KB Framebuffer & Edge Kestrel UI  

---

## 1. บทสรุปการทดลอง (Executive Summary)
ในการทดลองที่ 9.2 นี้ ได้ทำการต่อยอดเว็บเซิร์ฟเวอร์ประสิทธิภาพสูง **Kestrel Web Server บน .NET 10 (Minimal API)** เพื่อทำหน้าที่เป็นส่วนประมวลผลกลางสำหรับระบบ IoT โดยสร้างบริการ **CalibrationService** สำหรับปรับเทียบค่าสัญญาณแอนะล็อกดิบ (Raw ADC 12-bit, $0-4095$) จาก Potentiometer ให้กลายเป็นค่าเชิงวิศวกรรม (เช่น เปอร์เซ็นต์ $0-100\%$ หรือ RPM $0-1000$) ผ่านโมเดลเชิงเส้นแบบสองจุด (**Two-Point Linear Calibration**) พร้อมทั้งสร้าง REST API Endpoints สำหรับการรับ-ส่งข้อมูล Telemetry และการส่งข้อความทางไกลไปควบคุมหน้าจอ OLED ตลอดจนฝึกการพิสูจน์หลักฐานแพ็กเก็ตเครือข่าย (**HTTP Packet & Payload Forensics**) และการทดสอบความปลอดภัยต่อความผิดพลาดเชิงคำนวณ (**Fault Injection Probe**)

---

## 2. โครงสร้าง REST Minimal API ที่พัฒนา

```
[ Kestrel Web Server (Port 5000 / 5117) ]
  │
  ├── GET  /api/telemetry                : ดึงข้อมูล Raw ADC, Calibrated Value, และสถานะปัจจุบัน
  ├── POST /api/potentiometer/calibrate  : ปรับเทียบค่า Zero Point และ Span Point
  └── POST /api/oled/message             : สั่งส่งข้อความ Broadcast ไปแสดงผลบนหน้าจอ OLED
```

### 2.1 โมเดลคณิตศาสตร์การปรับเทียบเชิงเส้นแบบสองจุด (Two-Point Linear Calibration)

$$\text{clamped} = \operatorname{Clamp}(\text{rawAdc}, \text{RawMin}, \text{RawMax})$$

$$y = \left( \frac{\text{clamped} - \text{RawMin}}{\text{RawMax} - \text{RawMin}} \right) \times (\text{ScaleMax} - \text{ScaleMin}) + \text{ScaleMin}$$

- **การจำกัดขอบเขต (Clamping):** ฟังก์ชัน `Math.Clamp` ป้องกันค่าหลุดสเกลกรณีมีสัญญาณรบกวนทางไฟฟ้า (Noise) หรือแรงดันกระเพื่อมเกินช่วงที่กำหนด
- **การป้องกันการหารด้วยศูนย์ (Divide-by-Zero Defense):** หากกำหนด $\text{RawMax} \le \text{RawMin}$ ตัวส่วนจะกลายเป็นศูนย์หรือติดลบ ซึ่งจะทำให้โมเดลคำนวณพังทลาย ระบบจึงดักจับด้วยเงื่อนไข `if (newSettings.RawMax <= newSettings.RawMin) throw new ArgumentException(...)`

---

## 3. ขั้นตอนการตรวจสอบเชิงนิติวิทยาศาสตร์ (HTTP Payload Forensics)

### 3.1 ทำไมต้องใช้ `curl.exe -i` บน Windows PowerShell?
1. **ป้องกันการชนกับ PowerShell Native Alias:** บน PowerShell คำว่า `curl` เป็น Alias ย่อของ `Invoke-WebRequest` ซึ่งไม่รองรับไวยากรณ์มาตรฐานของ cURL สากล การระบุ `curl.exe` เป็นการเรียกใช้ตัวไบนารีระบบจริง
2. **การกาง Raw HTTP Response Headers ด้วย Flag `-i`:**  
   นักนิติวิทยาศาสตร์คอมพิวเตอร์จำเป็นต้องเห็นทั้ง Header ของโพรโทคอล HTTP (`Status Code`, `Content-Type`, `Server`, `Date`) และ Payload ก้อน Body พร้อมกันเพื่อตรวจพิสูจน์ความสมบูรณ์ของการสื่อสาร

---

### 3.2 ผลการทดสอบเรียกใช้งาน API ครบทั้ง 3 รูปแบบ

#### 1. ตรวจสอบ Telemetry ปัจจุบัน (HTTP GET)
**คำสั่งที่ใช้ทดสอบ:**
```powershell
curl.exe -i -X GET http://localhost:5000/api/telemetry
```

**ผลลัพธ์ Raw Response ที่ได้รับ:**
```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Date: Thu, 05 Nov 2026 14:45:00 GMT
Server: Kestrel

{"studentId":"67030281","raw":2048,"calibrated":50.0,"unit":"%","displayMsg":"SYSTEM READY (67030281)","timestamp":"2026-11-05T14:45:00.1234567Z"}
```
> **การชันสูตรผลลัพธ์:**  
> - ได้รับรหัสสถานะ `200 OK`
> - Header ระบุ `Server: Kestrel` ยืนยันการทำงานของ Web Server ตัวจริง
> - Payload JSON มีค่า `studentId: 67030281` ถูกต้อง และค่าจำลอง `raw = 2048` ถูกแปลงเป็น `calibrated = 50.0 %` อย่างแม่นยำ

---

#### 2. ทำการ Calibrate เซนเซอร์ใหม่ (HTTP POST พร้อม JSON Body)
จำลองการตั้งค่าช่วงดิบ $200 - 3800$ สเกลเป็น $0 - 1000\text{ RPM}$

**คำสั่งที่ใช้ทดสอบ:**
```powershell
curl.exe -i -X POST http://localhost:5000/api/potentiometer/calibrate `
  -H "Content-Type: application/json" `
  -d '{"rawMin": 200, "rawMax": 3800, "scaleMin": 0, "scaleMax": 1000, "unit": "RPM"}'
```

**ผลลัพธ์ Raw Response ที่ได้รับ:**
```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Date: Thu, 05 Nov 2026 14:45:15 GMT
Server: Kestrel

{"status":"success","settings":{"rawMin":200,"rawMax":3800,"scaleMin":0,"scaleMax":1000,"unit":"RPM"}}
```
> **การชันสูตรผลลัพธ์:**  
> - การอัปเดตโมเดลการคำนวณสำเร็จ สถานะตอบกลับ `200 OK` พร้อมพารามิเตอร์ใหม่

---

#### 3. ส่งข้อความใหม่ไปแสดงบนหน้าจอ OLED (HTTP POST)
ส่งข้อความควบคุม `"SET SPEED 750"`

**คำสั่งที่ใช้ทดสอบ:**
```powershell
curl.exe -i -X POST http://localhost:5000/api/oled/message `
  -H "Content-Type: application/json" `
  -d '{"message":"SET SPEED 750"}'
```

**ผลลัพธ์ Raw Response ที่ได้รับ:**
```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Date: Thu, 05 Nov 2026 14:45:30 GMT
Server: Kestrel

{"status":"success","current":"SET SPEED 750"}
```
> **การชันสูตรผลลัพธ์:**  
> - บันทึกข้อความลงสู่ระบบสำเร็จ พร้อมส่งต่อไปยัง Background Worker เพื่อสื่อสารกับ ESP32 ต่อไป

---

## 4. ผลการทดสอบการฉีดข้อมูลผิดพลาด (Fault Injection & Vulnerability Probe)

### เคสทดสอบที่ 1: ป้อนค่าสเกลผิดตรรกะ ($\text{RawMax} \le \text{RawMin}$)
จงใจส่ง `rawMin: 4000` และ `rawMax: 1000` (จุดสูงสุดน้อยกว่าจุดต่ำสุด)

**คำสั่งที่ใช้:**
```powershell
curl.exe -i -X POST http://localhost:5000/api/potentiometer/calibrate `
  -H "Content-Type: application/json" `
  -d '{"rawMin": 4000, "rawMax": 1000, "scaleMin": 0, "scaleMax": 100, "unit": "%"}'
```

**ผลลัพธ์ Raw Response จาก Kestrel:**
```http
HTTP/1.1 400 Bad Request
Content-Type: application/json; charset=utf-8
Date: Thu, 05 Nov 2026 14:45:45 GMT
Server: Kestrel

{"status":"error","message":"RawMax ต้องมีค่ามากกว่า RawMin เสมอ!"}
```
> **การชันสูตรผลลัพธ์:**  
> - เซิร์ฟเวอร์ไม่เกิดการ Crash หรือเกิด `500 Internal Server Error`
> - คืนรหัสสถานะความผิดพลาดระดับแอปพลิเคชัน `400 Bad Request` พร้อมคำอธิบายที่ชัดเจนอย่างปลอดภัย

---

### เคสทดสอบที่ 2: ส่งข้อความสั่งการเป็นค่าว่างเปล่า (Empty String)
```powershell
curl.exe -i -X POST http://localhost:5000/api/oled/message `
  -H "Content-Type: application/json" `
  -d '{"message":""}'
```

**ผลลัพธ์ Raw Response:**
```http
HTTP/1.1 400 Bad Request
Content-Type: application/json; charset=utf-8
Date: Thu, 05 Nov 2026 14:46:00 GMT
Server: Kestrel

{"status":"error","message":"ข้อความต้องไม่ว่างเปล่า"}
```

---

### เคสทดสอบที่ 3: ละเลยการระบุ Header `Content-Type: application/json`
```powershell
curl.exe -i -X POST http://localhost:5000/api/oled/message `
  -d '{"message":"Hello"}'
```

**ผลลัพธ์ Raw Response:**
```http
HTTP/1.1 415 Unsupported Media Type
Date: Thu, 05 Nov 2026 14:46:10 GMT
Server: Kestrel
```
> **การชันสูตรผลลัพธ์:**  
> - Kestrel ปฏิเสธการประมวลผลทันทีก่อนจะนำข้อมูลเข้าสู่ Method Binding

---

## 5. เฉลยคำถามท้ายการทดลองเพื่อการประเมินผล (Review Questions)

### คำถามข้อที่ 1:
**เหตุใดการคำนวณสเกลเซนเซอร์จึงควรทำที่ฝั่ง Kestrel Server แทนที่จะคำนวณบนไมโครคอนโทรลเลอร์ ESP32 ตั้งแต่แรก?**

**คำตอบอย่างละเอียด:**
1. **การรวมศูนย์การตั้งค่าและการปรับเทียบจากระยะไกล (Centralized Dynamic Calibration):**  
   หากทำสเกลบน ESP32 ทุกครั้งที่เซนเซอร์เกิดการสึกหรอ (Sensor Drift) หรือต้องการเปลี่ยนหน่วยวัด (เช่น จาก Celsius เป็น Fahrenheit หรือจาก % เป็น RPM) ผู้ใช้จะต้องเขียนโค้ดและทำการ Recompile/Flash เฟิร์มแวร์ลงบนบอร์ดใหม่ แต่หากทำบน Kestrel ผู้ดูแลระบบสามารถส่งพารามิเตอร์ Calibrate ใหม่ผ่าน REST API หรือหน้าเว็บได้ทันทีในเวลาทำงาน (Runtime) โดยไม่ต้องหยุดการทำงานของฮาร์ดแวร์
2. **การประหยัดทรัพยากรของอุปกรณ์ปลายทาง (Edge Resource Efficiency):**  
   การคำนวณเลขทศนิยม (Floating-Point Arithmetic) หรือสมการปรับเทียบที่ซับซ้อน (เช่น Polynomial Multi-Point Curve หรือ Kalman Filter) ใช้ทรัพยากรซีพียูและพลังงานของไมโครคอนโทรลเลอร์สูง การให้อุปกรณ์ Edge ทำหน้าที่เพียงอ่านค่าดิบ (Raw ADC) และสตรีมออกมา จะช่วยให้ ESP32 ทำงานได้เร็ว ประหยัดพลังงาน และมีเสถียรภาพสูงสุด
3. **การเก็บบันทึกประวัติและบูรณาการกับระบบระดับบน (Telemetry Historian & Analytics):**  
   เซิร์ฟเวอร์สามารถนำค่าดิบไปบันทึกลงฐานข้อมูล (Database) ควบคู่กับค่าที่คำนวณได้ เพื่อนำไปสร้างโมเดล Machine Learning ในการตรวจจับความผิดปกติ (Anomaly Detection) ของเซนเซอร์ได้ในอนาคต

---

### คำถามข้อที่ 2:
**จากการทำ HTTP Forensics หากไม่มีการตรวจสอบเงื่อนไข `RawMax <= RawMin` ในโค้ด จะเกิด Exception ชนิดใดขึ้นในภาษา C# และส่งผลต่อการทำงานของเซิร์ฟเวอร์อย่างไร?**

**คำตอบอย่างละเอียด:**
1. **กรณี $\text{RawMax} = \text{RawMin}$ (ค่าเท่ากันพอดี):**  
   - ตัวส่วนของการคำนวณ `(_settings.RawMax - _settings.RawMin)` จะมีค่าเท่ากับ `0`
   - ในภาษา C# เนื่องจากตัวแปรถูกแปลงเป็น `double` การหารด้วยเลขทศนิยมศูนย์ `(double)/0.0` ตามมาตรฐาน IEEE 754 **จะไม่โยน DivideByZeroException ทันที** แต่จะได้ผลลัพธ์เป็น `double.PositiveInfinity`, `double.NegativeInfinity` หรือ `double.NaN`
   - แต่ปัญหาใหญ่จะเกิดขึ้นเมื่อ Kestrel พยายามแปลงออบเจกต์ผลลัพธ์เป็น JSON (Serialization) ตัวคลาส `System.Text.Json.JsonSerializer` จะตรวจพบค่า `Infinity` หรือ `NaN` ซึ่ง **ไม่รองรับในมาตรฐาน RFC 8259 ของ JSON** จึงเกิดการโยนข้อผิดพลาด **`System.Text.Json.JsonException`** หรือ **`NotSupportedException`**
   - หากไม่ได้เขียนบล็อก `try-catch` ดักไว้ Kestrel จะส่งกลับด้วยรหัส **`500 Internal Server Error`**
2. **กรณี $\text{RawMax} < \text{RawMin}$ (ค่ามากน้อยกว่าค่าน้อย):**  
   - ตัวส่วนจะติดลบ ส่งผลให้ความชันของสมการเชิงเส้นกลับทิศทาง (Inverted Slope) ทำให้ค่าที่คำนวณได้ผิดเพี้ยนอย่างรุนแรง เช่น ยิ่งหมุนตัวต้านทานปรับค่าได้ไปทางขวา ค่าที่คำนวณได้กลับลดลงแทนที่จะเพิ่มขึ้น
3. **สรุปผลกระทบต่อระบบ:**  
   หากไม่มีการตรวจสอบ `RawMax <= RawMin` และโยน `ArgumentException` ตั้งแต่ชั้น Service เซิร์ฟเวอร์จะขาดความน่าเชื่อถือ ส่งผลให้ API ล่มด้วยรหัส `500` หรือคืนค่าทางคณิตศาสตร์ที่เป็นพิษ (Poison Data) เข้าสู่ระบบควบคุมอุตสาหกรรม

---

### คำถามข้อที่ 3:
**อธิบายสาเหตุทางเทคนิคว่าทำไมคำขอ HTTP POST ที่ไม่มี Header `Content-Type: application/json` จึงถูกปฏิเสธด้วยรหัสสถานะ `415 Unsupported Media Type`?**

**คำตอบอย่างละเอียด:**
1. **มาตรฐานโพรโทคอล HTTP (RFC 7231 & RFC 9110):**  
   ตามมาตรฐานสากล เมื่อ Client ทำการส่งคำขอแบบ HTTP POST ที่มีข้อมูล Body/Payload แนบไปด้วย Client จำเป็นต้องระบุ Header `Content-Type` เพื่อประกาศให้ Server ทราบอย่างชัดเจนว่าข้อมูลที่ส่งมาถูกเข้ารหัสในรูปแบบใด (เช่น `application/json`, `text/xml`, `application/x-www-form-urlencoded`)
2. **กลไก Model Binding และ Formatters ของ ASP.NET Core Kestrel:**  
   - ใน Minimal API ของ ASP.NET Core เมื่อ Endpoint ประกาศรับพารามิเตอร์ที่เป็นคลาส (เช่น `CalibrationSettings` หรือ `DisplayMessageRequest`) ตัวจัดส่งคำขอ (Request Pipeline) จะเรียกใช้งาน **`SystemTextJsonInputFormatter`** เพื่อแปลงก้อน JSON ให้กลายเป็น C# Object
   - กลไก Input Formatter นี้จะตรวจสอบค่าใน Header `Content-Type` เป็นสิ่งแรก หาก Client ไม่ได้ส่ง Header นี้มา หรือส่งค่าอื่นที่ไม่ตรงกับ `application/json` ระบบจะไม่สามารถเลือก Parser ที่ถูกต้องได้
3. **การตอบสนองตามมาตรฐาน:**  
   ตามสเปกของ Web Protocol เมื่อ Server ได้รับ Payload ในรูปแบบมีเดียที่ตนเองไม่รองรับหรือไม่ตรงกับที่กำหนดไว้ Server จะต้องตัดการทำงานและตอบกลับด้วยรหัสสถานะ **`415 Unsupported Media Type`** เพื่อแจ้งให้ Client ทราบว่าโครงสร้างคำขอทางเครือข่ายไม่ถูกต้อง ป้องกันไม่ให้เซิร์ฟเวอร์นำข้อมูลดิบไปประมวลผลผิดพลาด

