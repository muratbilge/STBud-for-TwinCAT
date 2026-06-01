"""Patch PE subsystem from CONSOLE (3) to WINDOWS (2) to prevent console window flash.
Usage: python tools/patch_subsystem.py <exe_path>
"""
import struct
import sys

def patch_subsystem(path):
    with open(path, 'r+b') as f:
        data = f.read()

    # Find PE header offset at offset 0x3C
    if len(data) < 0x40:
        print(f'File too small: {path}')
        return False

    pe_offset = struct.unpack_from('<I', data, 0x3C)[0]

    # Check PE signature
    if data[pe_offset:pe_offset+4] != b'PE\0\0':
        print(f'Not a valid PE file: {path}')
        return False

    # COFF header starts at pe_offset + 4
    coff_offset = pe_offset + 4

    # Optional header starts at coff_offset + 20
    opt_offset = coff_offset + 20

    # Optional header magic
    magic = struct.unpack_from('<H', data, opt_offset)[0]
    print(f'PE magic: 0x{magic:X}')

    # Subsystem offset from start of optional header
    # PE32 (0x10B): subsystem at +68
    # PE32+ (0x20B): subsystem at +68
    subsystem_offset_from_opt = 68
    subsystem_offset = opt_offset + subsystem_offset_from_opt

    subsystem = struct.unpack_from('<H', data, subsystem_offset)[0]
    print(f'Subsystem at offset {subsystem_offset}: {subsystem}')

    if subsystem == 2:
        print(f'Already WINDOWS subsystem: {path}')
        return True

    if subsystem == 3:
        # Patch CONSOLE -> WINDOWS
        patched = bytearray(data)
        struct.pack_into('<H', patched, subsystem_offset, 2)
        with open(path, 'wb') as f:
            f.write(patched)
        print(f'Patched subsystem CONSOLE(3) -> WINDOWS(2): {path}')
        return True

    print(f'Unexpected subsystem {subsystem}, trying to find actual subsystem field...')

    # For .NET assemblies, the actual subsystem might be at a different offset
    # Search for IMAGE_SUBSYSTEM_WINDOWS_CUI (3) near the optional header
    for off in range(opt_offset, min(opt_offset + 300, len(data) - 2)):
        val = struct.unpack_from('<H', data, off)[0]
        if val == 3:
            # Check if this is likely the subsystem field by verifying nearby fields
            print(f'Found possible subsystem value 3 at offset {off}')
            break

    return False

if __name__ == '__main__':
    if len(sys.argv) != 2:
        print('Usage: python patch_subsystem.py <exe_path>')
        sys.exit(1)
    sys.exit(0 if patch_subsystem(sys.argv[1]) else 1)