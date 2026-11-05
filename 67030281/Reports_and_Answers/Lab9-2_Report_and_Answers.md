# รายงานผลการทดลองและเฉลยคำตอบ ใบงานที่ 9.2 (Lab 9.2)
## การพัฒนาเอนจินปรับเทียบเซนเซอร์และ API ควบคุมการแสดงผลบน Kestrel Web Server พร้อมการพิสูจน์หลักฐานเครือข่าย (HTTP Payload Forensics)

**รหัสนักศึกษา:** 67030281    
**สัปดาห์ที่:** 09 - SPI OLED Display Architecture, 1KB Framebuffer & Edge Kestrel UI  

---

## 1. วัตถุประสงค์และภาพรวมการทดลอง
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
   ในการตรวจสอบการสื่อสารจำเป็นต้องเห็นทั้ง Header ของโพรโทคอล HTTP (`Status Code`, `Content-Type`, `Server`, `Date`) และ Payload ก้อน Body พร้อมกันเพื่อตรวจพิสูจน์ความถูกต้องของการส่งข้อมูล

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

{"raw":2048,"calibrated":50.0,"unit":"%","displayMsg":"SYSTEM READY","timestamp":"2026-11-05T14:45:00.1234567Z"}
```
> **ผลการสังเกตและวิเคราะห์ผลลัพธ์:**  
> - ได้รับรหัสสถานะ `200 OK`
> - Header ระบุ `Server: Kestrel` ยืนยันการทำงานของ Web Server ตัวจริง
> - Payload JSON แสดงค่าจำลอง raw = 2048 ถูกคำนวณสเกลเป็น calibrated = 50.0 % อย่างแม่นยำ

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
> **ผลการสังเกตและวิเคราะห์ผลลัพธ์:**  
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
> **ผลการสังเกตและวิเคราะห์ผลลัพธ์:**  
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
> **ผลการสังเกตและวิเคราะห์ผลลัพธ์:**  
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
> **ผลการสังเกตและวิเคราะห์ผลลัพธ์:**  
> - Kestrel ปฏิเสธการประมวลผลทันทีก่อนจะนำข้อมูลเข้าสู่ Method Binding

---

## 5. เฉลยคำถามท้ายการทดลองเพื่อการประเมินผล (Review Questions)

### คำถามข้อที่ 1:
**เหตุใดการคำนวณสเกลเซนเซอร์จึงควรทำที่ฝั่ง Kestrel Server แทนที่จะคำนวณบนไมโครคอนโทรลเลอร์ ESP32 ตั้งแต่แรก?**

**คำตอบ:**
1. **จูนค่าเซนเซอร์และเปลี่ยนหน่วยวัดได้ง่ายผ่านหน้าเว็บ/API (Centralized Dynamic Calibration):**  
   ถ้าเราเอาสูตรแปลงค่าไปเขียนตายตัวอยู่ในโค้ดของ ESP32 เวลาเซนเซอร์เสื่อมสภาพ ค่าเกิดเลื่อน (Drift) หรือเราอยากเปลี่ยนหน่วยวัด (เช่น อยากเปลี่ยนจาก 0-100% ไปเป็น 0-1000 RPM) เราจะต้องมานั่งต่อสาย ดึงบอร์ดมาคอมไพล์โค้ดแล้วแฟลชเฟิร์มแวร์ใหม่ทุกครั้ง แต่ถ้าเราให้บอร์ดส่งแค่ค่าดิบ (Raw ADC) ขึ้นมา แล้วให้ Kestrel บนเซิร์ฟเวอร์เป็นคนคิดสูตร เราก็จะสามารถปรับจูนค่า Zero/Span หรือเปลี่ยนหน่วยวัดผ่านหน้าเว็บหรือยิง REST API ได้สดๆ ทันที (Runtime) โดยไม่ต้องหยุดการทำงานของบอร์ด ESP32 เลยครับ
2. **ช่วยประหยัดแรงซีพียูและแรมของ ESP32:**  
   การคำนวณตัวเลขทศนิยม (Floating-Point) หรือถ้าในอนาคตมีสูตรปรับเทียบยากๆ เช่น สมการหลายจุด (Polynomial) หรือการใส่ฟิลเตอร์กรองสัญญาณ มันกินพลังการประมวลผลของไมโครคอนโทรลเลอร์ค่อนข้างเยอะ การให้ ESP32 ทำหน้าที่แค่อ่านค่าแอนะล็อกดิบแล้วยิงออกมาอย่างเดียว จะช่วยให้บอร์ดทำงานได้เร็ว ไม่หน่วง กินไฟน้อย และเสถียรที่สุดครับ
3. **เก็บบันทึกข้อมูลและเอาไปต่อยอดทำระบบวิเคราะห์ได้ง่าย:**  
   ฝั่งเซิร์ฟเวอร์มีฮาร์ดดิสก์และแรมเยอะกว่ามาก สามารถเก็บทั้งค่าดิบ (Raw) และค่าที่สเกลแล้วลง Database เอาไปพล็อตกราฟดูย้อนหลัง หรือเอาไปทำโมเดลแจ้งเตือนความผิดปกติของเซนเซอร์ในอนาคตได้สบายเลยครับ

---

### คำถามข้อที่ 2:
**จากการทำ HTTP Forensics หากไม่มีการตรวจสอบเงื่อนไข `RawMax <= RawMin` ในโค้ด จะเกิด Exception ชนิดใดขึ้นในภาษา C# และส่งผลต่อการทำงานของเซิร์ฟเวอร์อย่างไร?**

**คำตอบ:**
1. **ถ้าเกิดกรณี `RawMax == RawMin` (ค่าเท่ากันเป๊ะ):**  
   - ในสูตรคำนวณ ตัวส่วน `(RawMax - RawMin)` มันจะกลายเป็น `0` ทันที
   - ทีนี้ในภาษา C# พอตัวแปรถูกแปลงเป็นเลขทศนิยม `double` แล้วไปหารด้วย 0.0 ตามมาตรฐาน IEEE 754 **มันจะไม่โยน DivideByZeroException ออกมาทันทีเหมือนเลขจำนวนเต็ม (int)** แต่ผลลัพธ์ที่ได้จะได้ออกมาเป็นค่า `double.PositiveInfinity` หรือ `double.NaN` (Not a Number) แทนครับ
   - โค้ดคำนวณจะไม่พังทันที แต่ปัญหาใหญ่จะไปเกิดตอนที่ Kestrel พยายามแปลงออบเจกต์ผลลัพธ์ให้เป็น JSON เพื่อส่งกลับให้ผู้ใช้ (ขั้นตอน Serialization) ตัว `System.Text.Json` จะตรวจเจอค่า `Infinity` หรือ `NaN` ซึ่งมาตรฐาน JSON ทั่วไปไม่ยอมรับ ทำให้มันโยนข้อยกเว้น **`System.Text.Json.JsonException`** (หรือ `NotSupportedException`) ออกมา
   - ถ้าเราไม่ได้เขียนดัก `try-catch` ไว้ เซิร์ฟเวอร์ Kestrel ก็จะล่มและส่งรหัส **`500 Internal Server Error`** กลับไปให้ฝั่ง Client ทันทีครับ
2. **ถ้าเกิดกรณี `RawMax < RawMin` (ป้อนค่าสลับกัน ค่ามากดันน้อยกว่าค่าน้อย):**  
   - ตัวส่วนจะติดลบ ทำให้สูตรคณิตศาสตร์กลับทิศทาง (ความชันกลับด้าน) กลายเป็นว่ายิ่งบิดโวลุ่มเพิ่มขึ้น ค่าสเกลที่ได้กลับลดลงฮวบๆ ส่งผลให้ระบบควบคุมได้ค่าที่ผิดเพี้ยนไปหมด
3. **สรุป:** การที่เราเขียนเช็ค `if (RawMax <= RawMin)` แล้วสั่งโยน `ArgumentException` ตั้งแต่แรกในโค้ด จะทำให้เซิร์ฟเวอร์รู้ทันและตอบกลับผู้ใช้ด้วยรหัส **`400 Bad Request`** พร้อมข้อความเตือนชัดเจน ทำให้เซิร์ฟเวอร์ไม่แครช และระบบทำงานได้อย่างปลอดภัยครับ

---

### คำถามข้อที่ 3:
**อธิบายสาเหตุทางเทคนิคว่าทำไมคำขอ HTTP POST ที่ไม่มี Header `Content-Type: application/json` จึงถูกปฏิเสธด้วยรหัสสถานะ `415 Unsupported Media Type`?**

**คำตอบ:**
1. **ตามกฎกติกาของโปรโตคอล HTTP (RFC 7231):**  
   เวลาที่เราส่งคำขอแบบ HTTP POST ที่มีก้อนเนื้อหาข้อมูล (Body/Payload) แนบไปด้วย ตัว Client จะต้องแปะป้ายบอกเซิร์ฟเวอร์ผ่าน Header ที่ชื่อว่า `Content-Type` เสมอ เพื่อบอกให้เซิร์ฟเวอร์รู้ล่วงหน้าว่า ข้อมูลที่เราส่งมานั้นมันเป็นไฟล์ชนิดไหน เช่น เป็น JSON (`application/json`), เป็น XML หรือเป็นข้อมูลฟอร์ม
2. **การทำงานของ Kestrel (ตัวแกะข้อมูล Input Formatter):**  
   ใน ASP.NET Core Kestrel เมื่อ Endpoint ถูกเขียนให้รับพารามิเตอร์เป็นคลาสออบเจกต์ (เช่น `CalibrationSettings` หรือ `DisplayMessageRequest`) ตัว Kestrel จะต้องมองหาตัวแกะข้อมูลที่เหมาะสม ซึ่งก็คือ **`SystemTextJsonInputFormatter`** เพื่อมาถอดรหัสข้อความใน Body ให้กลายเป็น C# Object
   - ด่านแรกที่ Kestrel ใช้ตรวจสอบเลยก็คือ มันจะวิ่งไปเช็คที่ Header `Content-Type` ก่อนเพื่อน ถ้า Client ไม่ได้แนบ Header นี้มา หรือส่งค่าอื่นที่ไม่ใช่ `application/json` ทาง Kestrel จะไม่รู้ว่าจะต้องหยิบตัวแกะข้อมูลตัวไหนมาใช้
3. **เหตุผลที่โดนตอบกลับด้วยรหัส 415:**  
   พอ Kestrel ไม่รู้ว่าจะแกะข้อมูลด้วยวิธีไหน ตามมาตรฐานสากลมันจึงต้องตัดบทและตอบกลับทันทีด้วยรหัส **`415 Unsupported Media Type`** เพื่อบอกฝั่งคนส่งว่า 'ชนิดข้อมูลที่คุณส่งมา เซิร์ฟเวอร์ไม่รู้จักหรือไม่รองรับนะ' เพื่อความปลอดภัย ป้องกันไม่ให้เอาข้อมูลผิดประเภทเข้าไปยัดใส่ระบบจนแครชครับ

