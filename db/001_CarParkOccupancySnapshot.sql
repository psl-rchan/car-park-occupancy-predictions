/*
  Suggested table for a new database. Target framework of the API is net10.0.

  Stakeholder columns: CarParkCode, SnapshotTime, CountingCategory, Capacity.
  Occupied is an additional integer column (configurable name) required to
  compute occupancy percent = Occupied / Capacity * 100.

  If the table already exists, do not run this script. Map names in
  CarParkData:TableName and CarParkData:Columns instead.
*/
CREATE TABLE dbo.CarParkOccupancySnapshot
(
    CarParkCode NVARCHAR(64) NOT NULL,
    SnapshotTime DATETIME2(0) NOT NULL,
    CountingCategory NVARCHAR(64) NULL,
    Capacity INT NOT NULL,
    Occupied INT NOT NULL,
    CONSTRAINT CK_CarParkOccupancySnapshot_Capacity CHECK (Capacity >= 0),
    CONSTRAINT CK_CarParkOccupancySnapshot_Occupied CHECK (Occupied >= 0)
);

CREATE INDEX IX_CarParkOccupancySnapshot_Code_Time
    ON dbo.CarParkOccupancySnapshot (CarParkCode, SnapshotTime)
    INCLUDE (CountingCategory, Capacity, Occupied);
