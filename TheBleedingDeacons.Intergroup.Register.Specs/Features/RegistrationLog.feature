Feature: The registration log
  As the volunteer on the door
  I want every registration written somewhere the database cannot take with it
  So that a tablet that crashes mid-meeting does not lose who was here

  Each registration is written to the tablet's database and then appended
  to a log file, flushed to disk before the volunteer sees "Registered".
  The database is the record; the log is defence in depth, replayed if the
  database is lost between registering and finishing the meeting.

  Background:
    Given the group "Monday Step"
    And "Ann B" is a GSR of "Monday Step"

  Scenario: A registration is written to the log as well
    When "Monday Step" is registered
    Then the registration log says "Monday Step" is attending

  Scenario: A position that came with its group is logged too
    Given "Ann B" holds the position "Treasurer"
    When "Monday Step" is registered
    Then the registration log says "Treasurer" is attending

  Scenario: With the log switched off, only the database holds it
    Given the registration log is switched off
    When "Monday Step" is registered
    Then "Monday Step" is attending
    But the registration log is empty

  Scenario: A tablet that lost its database gets the meeting back from the log
    Given "Monday Step" has been registered with "Sam" standing in
    And the tablet's database loses today's registrations
    When the registration log is replayed
    Then "Monday Step" is attending with "Sam" standing in

  Scenario: The last thing said about a group is what counts
    Given "Monday Step" has been registered
    And "Monday Step" has been unregistered
    And the tablet's database loses today's registrations
    When the registration log is replayed
    Then "Monday Step" is not attending

  Scenario: A group the last sync removed is skipped, not resurrected
    Given "Monday Step" has been registered
    And Unity no longer lists "Monday Step"
    When the registration log is replayed
    Then the replay skipped 1 entry
