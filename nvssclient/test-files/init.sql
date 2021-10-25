CREATE DATABASE json_db;

CREATE TABLE message_status (
    id serial NOT NULL PRIMARY KEY,
    status TEXT
);

INSERT INTO message_status (status) VALUES 
    ('sent'),
    ('error'),
    ('acknowledged');

CREATE TABLE record (
    id serial NOT NULL PRIMARY KEY,
    info json NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE TABLE message (
    id serial NOT NULL PRIMARY KEY,
    uid VARCHAR (50) UNIQUE,
    state_auxiliary_id VARCHAR (50),
    cert_number INTEGER,
    nchs_id VARCHAR (50),
    record_id INTEGER,
    status_id INTEGER,
    retry INTEGER NOT NULL DEFAULT 0,
    response json, 
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    last_submission TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
    CONSTRAINT fk_record FOREIGN KEY(record_id) REFERENCES record(id),
    CONSTRAINT fk_status FOREIGN KEY(status_id) REFERENCES message_status(id)
);

