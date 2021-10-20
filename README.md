# Reference Client API
A reference implementation for jurisdications that retrieves json from a database and uses the vrdr-dotnet library to create vrdr records and wrap it in a message to submit to the NCHS API.

## Architecture Diagram 
<img src="resources/architecture.png" alt="drawing" width="750"/>  

### Architecture Description
The VRDR reporter send a new JSON vrdr record to the /messages endpoint which converts the json to a VRDR record, wraps it in a message and inserts it in the db. The timed service then pulls new messages from the db and POSTs the message to NCHS API. The reference API makes a GET request for any new messages from the server. It updates the database with the new message status. Finally, it checks for any messages that have not received a message in X amount of time and resubmits them. The service runs all of these steps in sequece every X number of seconds. The frequency is configurable.

TODO add description of how the library helps implement this.

TODO Configuration, server address, credentials

# Functions 
- Pull new records from the database
- Submit to api  
- Check for responses from the server - implemented with timer, 
- retry sending message that haven't had an responses in x time

- TODO send an ack when you get a coding response or extraction error
- single timer that calls all 3 in order
- check for responses before resending messages

- do they want to see all responses over time
- DBs: different dbs will have different implementations, keep queries generic and they could swap one to another? would we need to list out what we use? use adapters

# Use Cases
- demo use case
- reference use case
- recommendations for dashboards

# Setup
1. Setup the database docker containers
    a. Run `docker-compose up --build` to initialize the client db (postgres) and the server db (mssql)
    b. Run `dotnet ef migrations add InitialDb` to initialize, then `dotnet ef database update` TODO update , had to comment out section that adds the timed service?
2. Run the NCHS api server by following the README https://gitlab.mitre.org/nightingale/reference-nchs-api   
    a. skip the docker command, already accomplished in step 1
    b. change the start up command to use this pass word yourStrong$Password;
    d. from the reference-nchs-api/messaging project directory run `dotnet ef database update`
    e. run the api server with `dotnet run --project messaging`
3.  Now that the client db, server db, and the api server are all up and running, from the reference-client-api project root directory run
    ```
    dotnet run
    ```
## Migrations
1. When applying a new migration, update the models to reflect the desired changes. 
2. Stop and remove the container running the current db
3. Run `docker-compose up --build` to recreate the db
4. You may need to comment out this block in the timed service 
   ```                
    // .ConfigureServices((hostContext, services) =>
    // {
    //     services.AddHostedService<TimedHostedService>();
    // });
    ```
5. Run `dotnet ef migrations add <MigrationName>` to create the new migration
6. Run `dotnet ef database update` to update the db schema

Down the road questions
- What state will they want to keep track of? If they use a native format, they will have two versions of the same record
- Potential adapter for handling different formats?

Timeline:
- Have something solid when the api is deployed at NCHS, be able to submit messages for testing
- Demo to Rajesh and co, what do we need to authenticate to the server once its up
    - could test the authentication ahead of time
    - Last week of september