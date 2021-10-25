# import the connect library from psycopg2
from psycopg2 import connect
import os

table_name = "RecordItems"

# declare connection instance
conn = connect(
    dbname = "postgres",
    user = "postgres",
    host = "localhost",
    password = "mysecretpassword"
)

# declare a cursor object from the connection
cursor = conn.cursor()

# TODO read in the test data and insert into the db
directory = './json'
for filename in os.listdir(directory):
    if filename.endswith(".json"):
        name = os.path.join(directory, filename)
        f = open(name, "r")
        contents = f.read().replace('\n', '')
        sql = "INSERT INTO \"RecordItems\" (\"Record\", \"CreatedDate\", \"UpdatedDate\") VALUES(\'" + contents + "\', NOW(), NOW());"
        cursor.execute(sql)
        f.close()

# execute an SQL statement using the psycopg2 cursor object
cursor.execute(f"SELECT * FROM \"{table_name}\";")

# enumerate() over the PostgreSQL records
count = 0
for i, record in enumerate(cursor):
    print ("\n", type(record))
    #print ( record )
    count += 1
print("Number of records: " + str(count))

conn.commit()

# close the cursor object to avoid memory leaks
cursor.close()

# close the connection as well
conn.close()