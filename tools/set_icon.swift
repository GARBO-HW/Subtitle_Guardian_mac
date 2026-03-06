import Cocoa

let args = CommandLine.arguments

if args.count < 3 {
    print("Usage: swift set_icon.swift <image_path> <file_path>")
    exit(1)
}

let imagePath = args[1]
let filePath = args[2]

let image = NSImage(contentsOfFile: imagePath)

if let img = image {
    let success = NSWorkspace.shared.setIcon(img, forFile: filePath, options: [])
    if success {
        // print("Icon set successfully")
        exit(0)
    } else {
        fputs("Failed to set icon\n", stderr)
        exit(1)
    }
} else {
    fputs("Failed to load image from \(imagePath)\n", stderr)
    exit(1)
}