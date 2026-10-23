// Tiles every Nth frame of a folder into a contact sheet with frame numbers.
// Usage: sheet <frames dir> <out.png> <every> <columns> [tile width]
import AppKit
let a = CommandLine.arguments
let dir = a[1], out = a[2], every = Int(a[3])!, cols = Int(a[4])!, tw = a.count > 5 ? Int(a[5])! : 320
let files = try FileManager.default.contentsOfDirectory(atPath: dir).filter { $0.hasSuffix(".png") || $0.hasSuffix(".jpg") }.sorted()
let picks = stride(from: 0, to: files.count, by: every).map { files[$0] }
let th = tw * 9 / 16, rows = (picks.count + cols - 1) / cols
let ctx = CGContext(data: nil, width: cols * tw, height: rows * th, bitsPerComponent: 8, bytesPerRow: 0,
                    space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue)!
NSGraphicsContext.current = NSGraphicsContext(cgContext: ctx, flipped: false)
for (i, f) in picks.enumerated() {
    let src = CGImageSourceCreateWithURL(URL(fileURLWithPath: dir + "/" + f) as CFURL, nil)!
    let img = CGImageSourceCreateImageAtIndex(src, 0, nil)!
    let x = (i % cols) * tw, y = (rows - 1 - i / cols) * th
    ctx.draw(img, in: CGRect(x: x, y: y, width: tw, height: th))
    let label = NSAttributedString(string: String(f.dropFirst(6).prefix(5)), attributes: [.font: NSFont.boldSystemFont(ofSize: 14), .foregroundColor: NSColor.yellow])
    label.draw(at: NSPoint(x: x + 4, y: y + 4))
}
let rep = NSBitmapImageRep(cgImage: ctx.makeImage()!)
try rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: out))
print("sheet: \(picks.count) frames")
