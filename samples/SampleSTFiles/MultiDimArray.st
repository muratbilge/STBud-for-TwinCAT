TYPE ST_MultiDimArray :
STRUCT
    grid:ARRAY[1..10, 1..10] OF REAL;
    cube:ARRAY[0..2, 0..2, 0..2] OF DINT;
    names:ARRAY[1..5] OF STRING[20];
END_STRUCT
END_TYPE