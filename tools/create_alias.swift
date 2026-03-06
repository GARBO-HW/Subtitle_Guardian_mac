import Foundation
import Darwin

let args = CommandLine.arguments

if args.count < 3 {
    print("Usage: swift create_alias.swift <target> <alias>")
    exit(1)
}

let targetPath = args[1]
let aliasPath = args[2]

let targetURL = URL(fileURLWithPath: targetPath)
let aliasURL = URL(fileURLWithPath: aliasPath)

do {
    // Create bookmark data
    // .suitableForBookmarkFile
    let data = try targetURL.bookmarkData(options: .suitableForBookmarkFile, includingResourceValuesForKeys: nil, relativeTo: nil)
    
    // Write bookmark data
    try NSURL.writeBookmarkData(data, to: aliasURL, options: 0)
} catch {
    fputs("Error creating alias: \(error)\n", stderr)
    exit(1)
}