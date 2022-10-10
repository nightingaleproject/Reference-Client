// Dashboard to show the current state of records being submitted via
// FHIR Messaging, intended as a demonstration to provide visibility
// into how the data flows between systems
import React from "react";
import "./stylesheets/style.scss";
import {Button, Table} from "react-bootstrap";

class SubmitRecordsDashboard extends React.Component {

    constructor(props) {
      super(props)
      this.state = {
        records: {},
        messages: {},
        expandedRows: []
      };

    }

    handleMessageHistory = details => {
      console.log(details.message);
      console.log(details.responses);
    }
  
    handleExpand = record => {
      let newExpandedRows = [...this.state.expandedRows];
      let allExpanded = this.state.allExpanded;
      let idxFound = newExpandedRows.findIndex(id => {
        return id === record.id;
      });
  
      if (idxFound > -1) {
        console.log("Collapsing " + record.id + " " + idxFound);
        newExpandedRows.splice(idxFound, 1);
      } else {
        console.log("Expanding " + record.id);
        newExpandedRows.push(record.id);
      }
  
      console.log("Expanded rows");
      console.log(newExpandedRows);
  
      this.setState({ expandedRows: [...newExpandedRows] });
    };
  
    isExpanded = record => {
      const idx = this.state.expandedRows.find(id => {
        return id === record.id;
      });
  
      return idx > -1;
    };


    getRows = (id, record) => {
      let rows = [];
  
      const firstRow = (
        <tr key={record.id}>
        <td style={{textAlign: "center"}}>{record.id}</td>
        <td style={{textAlign: "center"}}>{record.certificateNumber}</td>
        <td style={{textAlign: "center"}}>{record.deathJurisdictionID}</td>
        <td style={{textAlign: "center"}}>{record.deathYear}</td>
        <td style={{textAlign: "center"}}>{record.status}</td>
        <button onClick={() => this.handleExpand(record)}>
          {this.isExpanded(record) ? "-" : "+"}
        </button>
      </tr>
      );
  
      rows.push(firstRow);

      if (this.isExpanded(record)) {
        let msgs = [...Object.entries(this.state.messages[id])] || [];
        if (msgs.length > 0) {
          let messageHeaderRow = (
            <tr>
              <th style={{textAlign: "center"}}>Type</th>
              <th style={{textAlign: "center"}}>Uri</th>
              <th style={{textAlign: "center"}}>Uid</th>
              <th style={{textAlign: "center"}}>Retries</th>
              <th style={{textAlign: "center"}}>Status</th>
              <th style={{textAlign: "center"}}>Details</th>
            </tr>
          );
          rows.push(messageHeaderRow);  
          let messageRows = [];
          for(let i = 0; i < msgs.length; i++) {
            let msg = JSON.parse(msgs[i][1].message.message);
            let row = (
              <tr className="message-details">
                <td className="message-details">Submission</td>
                <td className="message-details">{msg.entry[0].resource.eventUri}</td>
                <td className="message-details">{msgs[i][1].message.uid}</td>
                <td className="message-details">{msgs[i][1].message.retries}</td>
                <td className="message-details">{msgs[i][1].message.status}</td>
                <td></td>
              </tr>
            ); 
            messageRows.push(row);
            for(let j=0; j < msgs[i][1].responses.length; j++){
              let rsp = JSON.parse(msgs[i][1].responses[j]);
              let respRow = (
                <tr className="message-details">
                  <td className="message-details">Response</td>
                  <td className="message-details">{rsp.entry[0].resource.eventUri}</td>
                  <td className="message-details">{rsp.entry[0].resource.id}</td>
                  <td></td>
                  <td></td>
                  <td></td>
                </tr>
              ); 
              messageRows.push(respRow);
            }
          }
          rows.push(messageRows);
        }
      }
  
      return rows;
    };

    getRecordTable = records => {
      const recordRows = records.map(([key, value]) => {
        return this.getRows(key, value);
      });
      return (
        <Table striped bordered hover>
        <tr>
          <th style={{textAlign: "center"}}>Record Id</th>
            <th style={{textAlign: "center"}}>Certificate Number</th>
            <th style={{textAlign: "center"}}>Jurisdiction ID</th>
            <th style={{textAlign: "center"}}>Year</th>
            <th style={{textAlign: "center"}}>Status</th>
            <th style={{textAlign: "center"}}>Message History</th>
          </tr>
          {recordRows}
        </Table>
      );
    };

    render() {  
      return (
        <main>
        <div>{this.getRecordTable(Object.entries(this.state.records))}</div> 
        </main> 
      );
    }

    async refresh() {
      await fetch('http://localhost:4300/record', {mode:'cors'})
        .then(response => response.json())
        .then(data => {
          this.setState({records: data})
        });
  
      // get the message history for all records  
      const records = Object.entries(this.state.records);  
      let allMessages = {};  
      records.map(([key, record]) => {
        
        const msgs = fetch('http://localhost:4300/record/'+record.deathYear+'/'+record.deathJurisdictionID+'/'+record.certificateNumber, {mode:'cors'})
        .then(response => response.json())
        .then(data => allMessages[key] = data)
        .catch(error => console.log(error));
      });      

      this.setState({ messages: allMessages });
    }
  
    componentDidMount() {
      this.refresh();
      this.timer = window.setInterval(() => this.refresh(), 80000)
    }
  
    componentWillUnmount() {
      window.clearInterval(this.timer)
    }
  
  }
export default SubmitRecordsDashboard