# Reference Client API
A reference client implementation for jurisdications that handles submitting VRDR FHIR Messages to an NVSS API server, reliable delivery (acknowledgements and retries), and message responses. The implementation follows the [FHIR Messaging IG](http://build.fhir.org/ig/nightingaleproject/vital_records_fhir_messaging_ig/branches/main/message.html). The client leverages the vrdr-dotnet library to create and parse FHIR Messages.

# Use Cases
This client can be used to test and demo sending messages to the NVSS API Server using the FHIR Messaging IG format. It can also be used a reference for jurisdictions building their own client implementation.

## Architecture Diagram 
<img src="resources/architecture.png" alt="drawing" width="750"/>  

### Architecture Description
The VRDR reporter sends a JSON vrdr record via POST to the client's `/messages` endpoint. Upon receival, the client converts the json to a VRDR record, wraps it in a FHIR Message and inserts it in the database MessageItem table. The client's TimedService pulls new messages from the MessageItem table every X seconds and POSTs the message to NVSS API Server. Next, the TimedService makes a GET request for any new messages from the NVSS API Server. The TimedService parses the response messages and stores them in the ResponseItems table. If there was an acknowledgement or error, it updates the MessageItem table with the new message status. Finally, the TimedService checks for any messages that have not received an acknowledgement in Y seconds and resubmits them. The TimedService runs all of these steps in sequence every X number of seconds. The frequencies of X and Y are configurable. The VRDR reporter sends a GET request to the `/messages` endpoint at any time to get the status of all messages.

# API Endpoints
The client implementation has 2 endpoints
1. `POST /messages` 
   1. Parameters: The `POST /messages` endpoint accepts a list of VRDR records as json
   2. Function: Wraps each record in a FHIR message and queues the message to be sent to the NVSS API Server
   3. Response: A successful request returns `204 No Content`
2. `GET /messages`
   1. Parameters: None
   2. Function: Gets all MessageItems in the client DB
   3. Response: A successful request returns `200 OK` and a JSON list of all MessageItems in the client DB

# Getting Started
The client implementation can run with a local development setup where all services are run locally, or an integrated development setup that connects to the development NVSS API Server. 

## Local Development Setup
1. Setup the database docker containers
    a. Run `docker-compose up --build` to initialize the client db (postgres) and the NVSS API Server db (mssql)
    b. From the reference-client-api/nvssclient directory, run `dotnet ef database update` to intialize the client's db
2. Download the reference-nchs-api code from https://gitlab.mitre.org/nightingale/reference-nchs-api   
    a. make sure the db password in appsettings.json matches the one set in the docker compose file
    b. from the reference-nchs-api/messaging project directory run `dotnet ef database update` to initialize the NVSS API Server's db
    c. from the reference-nchs-api directory run the NVSS API Server with `dotnet run --project messaging`
3.  Configure the client implementation to connect to the local NVSS API Server
    1.  Create an `appsettings.json` file from the `appsettings.json.sample` file
    2.  In `appsettings.json` set `"ClientDatabase"` to your database connection string
    3.  In `appsettings.json` set `"LocalTesting" : true`
    4.  In `appsettings.json` set `"LocalServer":"https://localhost:5001/bundles"`
4.  Now that the client db, NVSS API Server db, and the NVSS API Server are all up and running, go to the reference-client-api/nvssclient project directory and run
    ```
    dotnet run
    ```
## NVSS Development Setup
1. Setup the database docker containers
    a. Run `docker-compose up --build` to initialize the client db (postgres)
    b. From the reference-client-api/nvssclient directory, run `dotnet ef database update` to intialize the client's db
2.  Configure the client implementation to connect to the development NVSS API Server
    1. Create an `appsettings.json` file from the `appsettings.json.sample` file
    2. In `appsettings.json` set `"LocalTesting" : false`
    3. In `appsettings.json` set `"AuthServer": "https://apigw.cdc.gov/auth/oauth/v2/token"`
    4. In `appsettings.json` set `"AuthServer": "https://apigw.cdc.gov/OSELS/NCHS/NVSSFHIRAPI/Bundles"`
    5. In `appsettings.json` fill out the `"Authentication"` section to authenticate to the server via oauth, contact admin for your credentials
3.  Now that the client db is running and the configuration is complete, go to the reference-client-api/nvssclient project directory and run
    ```
    dotnet run
    ```

## Migrations
1. When applying a new migration, update the models to reflect the desired changes. 
2. Stop and remove the container running the current client db
3. Run `docker-compose up --build` to recreate the db
   1. You may need to comment out this block in the timed service 
    ```                
        // .ConfigureServices((hostContext, services) =>
        // {
        //     services.AddHostedService<TimedHostedService>();
        // });
        ```
4. Run `dotnet ef migrations add <Your-Migration-Name>` to create the new migration
5. Run `dotnet ef database update` to update the db schema

# Developer Notes and Justifications
- To persist data and make it available to the Jurisidction upon request, the full response message is stored in the ResponseItems table, rather than just the ID in a Message Log. The ResponseItem table serves as the Message Log, see FHIR Messaging IG](http://build.fhir.org/ig/nightingaleproject/vital_records_fhir_messaging_ig/branches/main/message.html)
- The `POST /message` end point does not return data because the submission and coding process takes to long to provide a synchronous response. The user can request the message status via the `GET /message` endpoint. 

Down the road tasks and questions
- Fix the issue when applying new migrations that requires temporarily commenting out the TimedService
- What state will the jurisdictions want to keep track of? If they use a native format, they will have two versions of the same record
- Potential adapter for handling different formats?
- TODO send an ack when you get a coding response or extraction error
- do they want to see all responses over time? should old messages be cleared out after an expiration date