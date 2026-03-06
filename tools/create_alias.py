import Cocoa
import sys
import os

def create_alias(target_path, alias_path):
    target_url = Cocoa.NSURL.fileURLWithPath_(target_path)
    alias_url = Cocoa.NSURL.fileURLWithPath_(alias_path)
    
    # Create bookmark data
    # NSURLBookmarkCreationSuitableForBookmarkFile = 1 << 10 (1024)
    options = 1024
    
    data, error = target_url.bookmarkDataWithOptions_includingResourceValuesForKeys_relativeToURL_error_(
        options, None, None, None
    )
    
    if data is None:
        print(f"Error creating bookmark data: {error}")
        return False
    
    # Write bookmark data to file
    # Cocoa.NSURL.writeBookmarkData_toURL_options_error_ is a class method in recent macOS?
    # Actually, it's a static method on NSURL.
    # In PyObjC: NSURL.writeBookmarkData_toURL_options_error_(data, alias_url, 0, None)
    
    success, error = Cocoa.NSURL.writeBookmarkData_toURL_options_error_(
        data, alias_url, 0, None
    )
    
    if not success:
        print(f"Error writing alias file: {error}")
        return False
        
    return True

if __name__ == "__main__":
    if len(sys.argv) != 3:
        print("Usage: create_alias.py <target_path> <alias_path>")
        sys.exit(1)
        
    target = os.path.abspath(sys.argv[1])
    alias = os.path.abspath(sys.argv[2])
    
    # Ensure target exists (optional, but good for alias)
    if not os.path.exists(target):
        print(f"Warning: Target path {target} does not exist.")
    
    if create_alias(target, alias):
        print("Alias created successfully")
    else:
        sys.exit(1)
