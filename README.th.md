[English](README.md) | **ไทย** | [简体中文](README.zh-Hans.md)

# AvatarTuner

AvatarTuner วิเคราะห์และปรับ Texture Memory ของ Avatar VRChat ใน Unity Editor โดยสร้างสำเนา Texture, Material และ Animation ตามความจำเป็น โดยไม่แก้ไข Asset ต้นฉบับ และไม่ลดจำนวน Triangle หรือรวม Mesh/Material

## การติดตั้งและสถานะ

เวอร์ชัน 2.0.0 อยู่ระหว่างเตรียมการและยังไม่ได้เผยแพร่ สำหรับการทดสอบให้ติดตั้ง checkout นี้เป็น local package ในโปรเจกต์ VCC ที่ใช้ Unity 2022.3.22f1 เวอร์ชันที่เผยแพร่แล้วอยู่ใน [VCC repository](https://awakenginexe.github.io/AvatarTuner/index.json) และ [GitHub Releases](https://github.com/awakenginexe/AvatarTuner/releases) โดยยังไม่ควรคาดหวังว่าจะมี 2.0.0 เครื่องมือไม่ต้องพึ่ง VRChat SDK ตอนคอมไพล์ และใช้ NDMF เป็นทางเลือกสำหรับการ Apply ตอน Build

## วิธีใช้

เปิด **Tools → AvatarTuner** เลือก Avatar ใน Scene และ Analyze จากนั้นเปิด Optimization Workspace ตั้งค่า Quick หรือ Advanced แล้วสั่งประเมิน Candidate ตรวจ Preview ของ Texture ที่เลือกและค่าหน่วยความจำประมาณก่อนยืนยัน การประเมินยกเลิกได้ ผลลัพธ์อยู่ใน `Assets/AvatarTuner-Generated/` และรองรับ Undo และ Restore ระบบใช้สำเนา Clip/Controller สำหรับ Material Swap ที่แปลงได้อย่างปลอดภัย และแจ้งกรณีที่ทำไม่ได้

การตั้งค่าเก็บแยกตาม Avatar ใน `Library` ของโปรเจกต์ เมื่อสร้าง Category หรือ Custom override จะคัดลอกค่าที่สืบทอด ณ เวลานั้น การเปลี่ยนค่าแม่ภายหลังไม่เปลี่ยน snapshot; Reset จะกลับไปสืบทอดค่า การเปลี่ยน Quick จะเก็บ Custom override ไว้

ระบบเลือก Candidate ที่เล็กที่สุดซึ่งผ่านเกณฑ์คุณภาพ เป้าหมายอัตราหน่วยความจำเป็นเพียงเป้าหมายและอาจทำไม่ถึง Standard ใช้ Unity Mitchell และ compression quality 50; Best Quality ใช้ quality 100 และเปรียบเทียบ Unity Mitchell/Bilinear รวมถึง Area, Lanczos3 และ Mitchell สำหรับ PNG/JPEG โดยใช้ Unity importer ไม่ใช้ encoder ภายนอก

HDR, Normal Map และรูปทรงที่กำกวมจะคงเดิมเมื่อไม่สามารถตรวจสอบ Candidate ที่ import ได้อย่างปลอดภัย BC6H รองรับการคำนวณและรายงานหน่วยความจำ แต่ไม่มีการแปลง HDR ใหม่ ASTC 4x4/6x6/8x8 ใช้กับ Android เท่านั้น โดยต้องเลือก Android build target และรองรับการถอดรหัส

ค่าจากแผนและค่าประมาณจากขนาด Format และ Mipmap ที่ import มีฐานต่างกัน Texture ที่ใช้ร่วมกันนับครั้งเดียว และข้อมูลที่ไม่ทราบทำให้ยอดไม่สมบูรณ์ ทั้งสองค่าไม่ใช่ผลวัด VRChat, ขนาดดาวน์โหลด หรือขนาดไฟล์ ตรวจหน่วย MiB และ MB ด้วย Manifest รุ่นเก่ายังอ่านและ Restore ได้ แต่ไม่ทราบฐานการวัดและผลจากเวอร์ชันเก่าถือว่าล้าสมัย ควร Optimize ใหม่

ยังไม่ได้ทดสอบ 2.0.0 ใน Unity หรือ VRChat โปรดใช้ [รายการทดสอบ](docs/Manual-Testing-2.0.0.md) และบันทึกผลจริง ดู [changelog](CHANGELOG.md), [third-party notices](THIRD_PARTY_NOTICES.md) และ [MIT license](LICENSE)
