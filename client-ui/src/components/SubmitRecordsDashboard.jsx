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
      // TODO: display the message contents to the user
    }


    handleExpand = record => {
      const expandedRows = this.state.expandedRows;
      if (expandedRows.includes(record.id)) {
        this.setState({ expandedRows: expandedRows.filter(id => id !== record.id) });
      } else {
        this.setState({ expandedRows: expandedRows.concat(record.id) });
      }
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
          {this.state.expandedRows.includes(record.id) ? "-" : "+"}
        </button>
      </tr>
      );
  
      rows.push(firstRow);

      if (this.state.expandedRows.includes(record.id) && this.state.messages[id] != null) {
        let msgs = [...Object.entries(this.state.messages[id])] || [];
        if (msgs.length > 0) {
          let messageHeaderRow = (
            <tr className="message-details-header">
              <th className="message-details-header" style={{textAlign: "center"}}>Type</th>
              <th className="message-details-header" style={{textAlign: "center"}}>Uri</th>
              <th className="message-details-header" style={{textAlign: "center"}}>Uid</th>
              <th className="message-details-header" style={{textAlign: "center"}}>Retries</th>
              <th className="message-details-header" style={{textAlign: "center"}}>Status</th>
              <th className="message-details-header" style={{textAlign: "center"}}>Date Created</th>
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
                <td className="message-details">{msgs[i][1].message.createdDate}</td>
              </tr>
            ); 
            messageRows.push(row);
            if (msgs[i][1].responses != null){
              for(let j=0; j < msgs[i][1].responses.length; j++){
                let rsp = JSON.parse(msgs[i][1].responses[j].message);
                let respRow = (
                  <tr className="message-details">
                    <td className="message-details">Response</td>
                    <td className="message-details">{rsp.entry[0].resource.eventUri}</td>
                    <td className="message-details">{rsp.entry[0].resource.id}</td>
                    <td className="message-details"></td>
                    <td className="message-details"></td>
                    <td className="message-details">{msgs[i][1].responses[j].createdDate}</td>
                  </tr>
                ); 
                messageRows.push(respRow);
              }
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
        // confirm parameters are not null before making request
        if (record.deathYear != null && record.deathJurisdictionID != null && record.certificateNumber != null)
        {
          const msgs = fetch('http://localhost:4300/record/'+record.deathYear+'/'+record.deathJurisdictionID+'/'+record.certificateNumber, {mode:'cors'})
          .then(response => response.json())
          .then(data => allMessages[key] = data)
          .catch(error => console.log(error));
        }
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