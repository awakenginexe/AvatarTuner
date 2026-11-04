[English](README.md) | **ไทย** | [简体中文](README.zh-Hans.md)

# AvatarTuner

AvatarTuner วิเคราะห์และปรับ Texture Memory ของ Avatar VRChat ใน Unity Editor โดยสร้างสำเนา Texture, Material และ Animation ตามความจำเป็น โดยไม่แก้ไข Asset ต้นฉบับ และไม่ลดจำนวน Triangle หรือรวม Mesh/Material

## การติดตั้งและสถานะ

เวอร์ชัน 2.0.0 อยู่ระหว่างเตรียมการและยังไม่ได้เผยแพร่ สำหรับการทดสอบให้ติดตั้ง checkout นี้เป็น local package ในโปรเจกต์ VCC ที่ใช้ Unity 2022.3.22f1 เวอร์ชันที่เผยแพร่แล้วอยู่ใน [VCC repository](https://awakenginexe.github.io/AvatarTuner/index.json) และ [GitHub Releases](https://github.com/awakenginexe/AvatarTuner/releases) โดยยังไม่ควรคาดหวังว่าจะมี 2.0.0 เครื่องมือไม่ต้องพึ่ง VRChat SDK ตอนคอมไพล์ และใช้ NDMF เป็นทางเลือกสำหรับการ Apply ตอน Build

## วิธีใช้

เปิด **Tools → AvatarTuner** เลือก Avatar ใน Scene และ Analyze แล้วกด **Optimize Avatar** เลือก **Standard** ให้ระบบเลือกเทกซ์เจอร์ รูปแบบ และความละเอียดอัตโนมัติ หรือ **Advanced** เพื่อเลือกเทกซ์เจอร์และกำหนดเป้าหมายรายรายการ การเปิดหน้าต่างหรือเปลี่ยนค่าไม่ประมวลผลเทกซ์เจอร์ กดประเมิน Candidate ตรวจค่าหน่วยความจำและการเปลี่ยนแปลง แล้วจึงยืนยันและปรับแต่ง การประเมินสร้างตัวเลือกชั่วคราวและยกเลิกได้ ผลลัพธ์อยู่ใน `Assets/AvatarTuner-Generated/` และรองรับ Undo และ Restore

Advanced บันทึกเทกซ์เจอร์ที่เลือกและค่าราย Avatar ใน `Library` โดยประเมินและปรับแต่งเฉพาะรายการที่เลือก การสร้าง Category หรือ Custom override จะคัดลอกค่าที่สืบทอด ณ เวลานั้น Reset จะกลับไปสืบทอดค่า การสลับโหมดเก็บค่า Advanced และข้อยกเว้นเดิมไว้

Standard เลือก Candidate ที่เล็กที่สุดซึ่งผ่านเกณฑ์คุณภาพ Advanced รับอัตราลดหน่วยความจำตั้งแต่ 1 ขึ้นไป รวมถึงทศนิยม และเลือกค่าประมาณตาม SDK ที่ใกล้เป้าหมายและผ่านเกณฑ์ที่สุด เช่น 4x คือเหลือหนึ่งในสี่ของหน่วยความจำเดิม รูปแบบและข้อจำกัดคุณภาพอาจทำให้ไม่ได้ค่าตรงเป้าหมาย ค่าเริ่มต้นใช้ Unity Mitchell และ compression quality 50 ส่วน Best Quality ใน Advanced ใช้ quality 100 และเปรียบเทียบตัวกรองเพิ่มเติม โดยใช้ Unity importer ไม่ใช้ encoder ภายนอก

HDR, Normal Map และรูปทรงที่กำกวมจะคงเดิมเมื่อไม่สามารถตรวจสอบ Candidate ที่ import ได้อย่างปลอดภัย BC6H รองรับการคำนวณและรายงานหน่วยความจำ แต่ไม่มีการแปลง HDR ใหม่ ASTC 4x4/6x6/8x8 ใช้กับ Android เท่านั้น โดยต้องเลือก Android build target และรองรับการถอดรหัส

หน่วยความจำ Texture ใช้สูตร Format/Mipmap และหน่วย MB ตาม VRChat SDK (ไบต์ / 1,048,576) ค่าของ Avatar ปัจจุบันใช้การคำนวณจาก SDK ที่ติดตั้งไว้เมื่ออ่านได้ มิฉะนั้นจะแสดงว่าเป็นค่าประมาณตามสูตร SDK ยอดตามแผนและจากผลที่ import ยังเป็นค่าประมาณ โดยนับ Texture ที่ใช้ร่วมกันครั้งเดียวและแจ้งเมื่อข้อมูลไม่ครบ รายการสำหรับวางแผนอาจรวม Texture จาก Animation ที่เกินกว่ารายการ Renderer ปัจจุบันของ SDK ค่าเหล่านี้ไม่ใช่ผลวัด Avatar ที่ Build แล้ว ขนาดดาวน์โหลด หรือขนาดไฟล์ ยอดหน่วยความจำเก่าต้อง Analyze ใหม่ โดยยังเก็บ Asset ที่สร้างและข้อมูล Restore ไว้

ยังไม่ได้ทดสอบ 2.0.0 ใน Unity หรือ VRChat โปรดใช้ [รายการทดสอบ](docs/Manual-Testing-2.0.0.md) และบันทึกผลจริง ดู [changelog](CHANGELOG.md), [third-party notices](THIRD_PARTY_NOTICES.md) และ [MIT license](LICENSE)
