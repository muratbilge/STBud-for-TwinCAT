TYPE ST_SensorArray :
STRUCT
    temperatures:ARRAY[1..16] OF REAL;
    pressures:ARRAY[1..8] OF LREAL;
    names:ARRAY[1..16] OF STRING[32];
    statusFlags:ARRAY[1..16] OF BOOL;
END_STRUCT
END_TYPE