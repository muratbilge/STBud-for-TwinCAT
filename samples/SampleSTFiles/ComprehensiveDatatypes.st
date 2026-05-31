TYPE ST_TcPOU :
STRUCT
    name:STRING[255];
    id:GUID;
    dataType:eDataType;
    size:DINT;
    flags:DWORD;
    offset:REAL;
    pData:POINTER TO BYTE;
    pNext:REF_TO ST_TcPOU;
    attributes:ARRAY[1..32] OF STRING[64];
    timestamps:ARRAY[1..32] OF DT;
    comment:WSTRING[512];
END_STRUCT
END_TYPE