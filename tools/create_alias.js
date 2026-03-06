ObjC.import("Foundation");

function run(argv) {
  if (argv.length < 2) {
    return 1;
  }

  var targetPath = argv[0];
  var aliasPath = argv[1];

  var targetURL = $.NSURL.fileURLWithPath(targetPath);
  var aliasURL = $.NSURL.fileURLWithPath(aliasPath);

  // NSURLBookmarkCreationSuitableForBookmarkFile = 1024
  var data =
    targetURL.bookmarkDataWithOptionsIncludingResourceValuesForKeysRelativeToURLError(
      1024,
      [],
      null,
      null
    );

  if (data == null) {
    return 1;
  }

  var success = $.NSURL.writeBookmarkDataToURLOptionsError(
    data,
    aliasURL,
    0,
    null
  );

  if (!success) {
    return 1;
  }

  return 0;
}
